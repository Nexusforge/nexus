using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Statistics;
using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal class AggregationService
    {
        #region Fields

        private ILogger _logger;

        #endregion

        #region Constructors

        public AggregationService(
            ILogger<AggregationService> logger)
        {
            _logger = logger;

            this.Progress = new Progress<double>();
        }

        #endregion

        #region Properties

        public Progress<double> Progress { get; }

        #endregion

        #region Methods

        public static List<AggregationInstruction> ComputeInstructions(AggregationSetup setup, CatalogState state, ILogger logger)
        {
            var catalogIds = setup.Aggregations
                .Select(aggregation => aggregation.CatalogId)
                .Distinct().ToList();

            return catalogIds.Select(catalogId =>
            {
                var container = state.CatalogCollection.CatalogContainers.FirstOrDefault(container => container.Id == catalogId);

                if (container is null)
                    return null;

                var backendSources = container.Catalog.Resources
                    .SelectMany(resource => resource.Representations.Select(representation => representation.BackendSource))
                    .Distinct()
                    .Where(backendSource => backendSource != state.AggregationBackendSource)
                    .ToList();

                return new AggregationInstruction(container, backendSources.ToDictionary(backendSource => backendSource, backendSource =>
                {
                    // find aggregations for catalog ID
                    var potentialAggregations = setup.Aggregations
                        .Where(parameters => parameters.CatalogId == container.Catalog.Id)
                        .ToList();

                    // create resource to aggregations map
                    var aggregationResources = container.Catalog.Resources

                        // find all resources for current reader backend source
                        .Where(resource => resource.Representations.Any(representation => representation.BackendSource == backendSource))

                        // find all aggregations for current resource
                        .Select(resource =>
                        {
                            return new AggregationResource()
                            {
                                Resource = resource,
                                Aggregations = potentialAggregations.Where(current => AggregationService.ApplyAggregationFilter(resource, current.Filters, logger)).ToList()
                            };
                        })
                        // take all resources with aggregations
                        .Where(aggregationResource => aggregationResource.Aggregations.Any());

                    return aggregationResources.ToList();
                }));
            }).Where(instruction => instruction != null).ToList();
        }

        public Task<string> AggregateDataAsync(string databaseFolderPath,
                                               AggregationSetup setup,
                                               CatalogState state,
                                               Func<BackendSource, Task<IDataSourceController>> getControllerAsync,
                                               CancellationToken cancellationToken)
        {
            if (setup.Begin != setup.Begin.Date)
                throw new ValidationException("The begin parameter must have no time component.");

            if (setup.End != setup.End.Date)
                throw new ValidationException("The end parameter must have no time component.");

            return Task.Run(async () =>
            {
                var progress = (IProgress<double>)this.Progress;
                var instructions = AggregationService.ComputeInstructions(setup, state, _logger);
                var days = (setup.End - setup.Begin).TotalDays;
                var totalDays = instructions.Count() * days;
                var i = 0;

                foreach (var instruction in instructions)
                {
                    var catalogId = instruction.Container.Id;

                    for (int j = 0; j < days; j++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var currentDay = setup.Begin.AddDays(j);
                        var progressMessage = $"Processing catalog '{catalogId}': {currentDay.ToString("yyyy-MM-dd")}";
                        var progressValue = (i * days + j) / totalDays;
                        progress.Report(progressValue);

                        await this.AggregateCatalogAsync(
                            databaseFolderPath,
                            catalogId,
                            currentDay,
                            state,
                            setup,
                            instruction,
                            getControllerAsync,
                            cancellationToken);
                    }

                    i++;
                }

                return string.Empty;
            }, cancellationToken);
        }

        private async Task AggregateCatalogAsync(string databaseFolderPath,
                                                 string catalogId,
                                                 DateTime date,
                                                 CatalogState state,
                                                 AggregationSetup setup,
                                                 AggregationInstruction instruction,
                                                 Func<BackendSource, Task<IDataSourceController>> getControllerAsync,
                                                 CancellationToken cancellationToken)
        {
            foreach (var (backendSource, aggregationResources) in instruction.DataReaderToAggregationsMap)
            {
                using var controller = await getControllerAsync(backendSource);

                // get files
                if (!await controller.IsDataOfDayAvailableAsync(catalogId, date, cancellationToken))
                    return;

                // catalog
                var container = state.CatalogCollection.CatalogContainers.FirstOrDefault(container => container.Id == catalogId);

                if (container == null)
                    throw new Exception($"The requested catalog '{catalogId}' could not be found.");

                var targetDirectoryPath = Path.Combine(databaseFolderPath, "DATA", WebUtility.UrlEncode(container.Id), date.ToString("yyyy-MM"), date.ToString("dd"));

                // for each resource
                foreach (var aggregationResource in aggregationResources)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var representation = aggregationResource.Resource.Representations.First();

                        var parameters = new object[]
                        {
                            targetDirectoryPath,
                            controller,
                            representation,
                            aggregationResource.Aggregations,
                            date,
                            setup.Force,
                            cancellationToken
                        };

                        await (Task)NexusCoreUtilities.InvokeGenericMethod(
                            this, 
                            nameof(this.OrchestrateAggregationAsync),
                            BindingFlags.Instance | BindingFlags.NonPublic,
                            NexusCoreUtilities.GetTypeFromNexusDataType(representation.DataType),
                            parameters);
                    }
                    catch (TaskCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex.GetFullMessage());
                    }
                }
            }
        }

        private async Task OrchestrateAggregationAsync<T>(string targetDirectoryPath,
                                                           DataSourceController dataSourceController,
                                                           CatalogItem catalogItem,
                                                           List<Aggregation> aggregations,
                                                           DateTime date,
                                                           bool force,
                                                           CancellationToken cancellationToken) where T : unmanaged
        {
            // prepare variables
            var units = new List<AggregationUnit>();
            var resource = catalogItem.Resource;

            // prepare buffers
            foreach (var aggregation in aggregations)
            {
                var periodsToSkip = new List<int>();

                foreach (var period in aggregation.Periods)
                {
                    foreach (var entry in aggregation.Methods)
                    {
                        var method = entry.Key;
                        var arguments = entry.Value;

                        // translate method name
                        var methodIdentifier = method switch
                        {
                            AggregationMethod.Mean => "mean",
                            AggregationMethod.MeanPolar => "mean_polar",
                            AggregationMethod.Min => "min",
                            AggregationMethod.Max => "max",
                            AggregationMethod.Std => "std",
                            AggregationMethod.Rms => "rms",
                            AggregationMethod.MinBitwise => "min_bitwise",
                            AggregationMethod.MaxBitwise => "max_bitwise",
                            AggregationMethod.SampleAndHold => "sample_and_hold",
                            AggregationMethod.Sum => "sum",
                            _ => throw new Exception($"The aggregation method '{method}' is unknown.")
                        };

                        var targetFileName = $"{resource.Id}_{period.ToUnitString()}_{methodIdentifier}.nex";
                        var targetFilePath = Path.Combine(targetDirectoryPath, targetFileName);

                        if (force || !File.Exists(targetFilePath))
                        {
                            var buffer = new double[TimeSpan.FromDays(1).Ticks / period.Ticks];

                            var unit = new AggregationUnit()
                            {
                                Aggregation = aggregation,
                                Period = period,
                                Method = method,
                                Argument = arguments,
                                Buffer = buffer,
                                TargetFilePath = targetFilePath
                            };

                            units.Add(unit);
                        }
                        else
                        {
                            // skip period / method combination
                        }
                    }
                }
            }

            if (!units.Any())
                return;

            // process data
            var endDate = date.AddDays(1);

            // read raw data
            var dataPipe = new Pipe();
            var statusPipe = new Pipe();

            var reading = dataSourceController.ReadSingleAsync(
                date,
                endDate,
                catalogItem,
                dataPipe.Writer,
                statusPipe.Writer,
                progress: default,
                _logger,
                cancellationToken);

            var writing = this.AggregateSingleAsync<T>(
                catalogItem, 
                dataPipe.Reader,
                statusPipe.Reader,
                units,
                cancellationToken);

            await Task.WhenAll(reading, writing);

            // write data to file
            foreach (var unit in units)
            {
                var tmpFilePath = Path.Combine($".temp_{unit.TargetFilePath}");

                try
                {
                    AggregationFile.Create<double>(tmpFilePath, unit.Buffer);

                    var lastException = default(Exception);

                    for (int i = 0; i < 10; i++)
                    {
                        try
                        {
                            File.Move(tmpFilePath, unit.TargetFilePath, overwrite: true);
                            break;
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            await Task.Delay(TimeSpan.FromSeconds(2));
                        }
                    }

                    if (lastException is not null)
                        _logger.LogError(lastException, "Unable to rename temporary file.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occured trying to create the aggregation file.");

                    try
                    {
                        if (File.Exists(tmpFilePath))
                            File.Delete(tmpFilePath);
                    }
                    catch (Exception ex2)
                    {
                        _logger.LogError(ex2, "Unable to delete temporary file.");
                    }

                    try
                    {
                        if (File.Exists(unit.TargetFilePath))
                            File.Delete(unit.TargetFilePath);
                    }
                    catch (Exception ex2)
                    {
                        _logger.LogError(ex2, "Unable to delete aggregation file.");
                    }
                }
            }
        }

        private async Task AggregateSingleAsync<T>(
            CatalogItem catalogItem,
            PipeReader dataReader,
            PipeReader statusReader, 
            List<AggregationUnit> units,
            CancellationToken cancellationToken)
            where T : unmanaged
        {
            while (true)
            {
                // read
                var dataResult = await dataReader.ReadAsync(cancellationToken);
                var dataSequence = dataResult.Buffer;

                var statusResult = await statusReader.ReadAsync(cancellationToken);
                var statusSequence = statusResult.Buffer;

                // aggregate
                var position = default(SequencePosition);

                while (dataResult.Buffer.TryGet(ref position, out var dataBuffer, advance: true) &&
                       statusResult.Buffer.TryGet(ref position, out var statusBuffer, advance: true))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var typedDataBuffer = new ReadonlyCastMemoryManager<byte, T>(dataBuffer).Memory;
                    this.ApplyAggregationFunction<T>(catalogItem.Representation, typedDataBuffer, statusBuffer, units);
                }

                // advance
                dataReader.AdvanceTo(dataSequence.Start, dataSequence.End);
                statusReader.AdvanceTo(statusSequence.Start, statusSequence.End);

                if (dataResult.IsCompleted || statusResult.IsCompleted)
                    break;
            }

            await dataReader.CompleteAsync();
            await statusReader.CompleteAsync();
        }

        private void ApplyAggregationFunction<T>(Representation representation,
                                                 ReadOnlyMemory<T> data,
                                                 ReadOnlyMemory<byte> status,
                                                 List<AggregationUnit> aggregationUnits) where T : unmanaged
        {
            var nanLimit = 0.99;
            var representation_double = default(double[]);

            foreach (var unit in aggregationUnits)
            {
                var aggregation = unit.Aggregation;
                var period = unit.Period;
                var method = unit.Method;
                var argument = unit.Argument;
                var sampleCount = period.Ticks / representation.SamplePeriod.Ticks;

                double[] partialBuffer;

                switch (unit.Method)
                {
                    case AggregationMethod.Mean:
                    case AggregationMethod.MeanPolar:
                    case AggregationMethod.Min:
                    case AggregationMethod.Max:
                    case AggregationMethod.Std:
                    case AggregationMethod.Rms:
                    case AggregationMethod.SampleAndHold:
                    case AggregationMethod.Sum:

                        if (representation_double == null)
                        {
                            representation_double = new double[data.Length];
                            BufferUtilities.ApplyRepresentationStatus(data, status, representation_double);
                        }

                        partialBuffer = this.ApplyAggregationFunction(method, argument, (int)sampleCount, representation_double, nanLimit, _logger);
                        break;

                    case AggregationMethod.MinBitwise:
                    case AggregationMethod.MaxBitwise:

                        partialBuffer = this.ApplyAggregationFunction(method, argument, (int)sampleCount, data, status, nanLimit, _logger);
                        break;

                    default:

                        _logger.LogWarning($"The aggregation method '{unit.Method}' is not known. Skipping period {period}.");

                        continue;
                }

                if (partialBuffer is not null)
                {
                    Array.Copy(partialBuffer, 0, unit.Buffer, unit.BufferPosition, partialBuffer.Length);
                    unit.BufferPosition += partialBuffer.Length;
                }
            }
        }

        internal double[] ApplyAggregationFunction(AggregationMethod method,
                                                   string argument,
                                                   int kernelSize,
                                                   ReadOnlyMemory<double> data,
                                                   double nanLimit,
                                                   ILogger logger)
        {
            var targetRepresentationLength = data.Length / kernelSize;
            var result = new double[targetRepresentationLength];

            switch (method)
            {
                case AggregationMethod.Mean:

                    Parallel.For(0, targetRepresentationLength, x =>
                    {
                        var chunkData = this.GetNaNFreeData(data, x, kernelSize);
                        var isHighQuality = (chunkData.Length / (double)kernelSize) >= nanLimit;

                        if (isHighQuality)
                            result[x] = ArrayStatistics.Mean(chunkData);
                        else
                            result[x] = double.NaN;
                    });

                    break;

                case AggregationMethod.MeanPolar:

                    double[] sin = new double[targetRepresentationLength];
                    double[] cos = new double[targetRepresentationLength];
                    double limit;

                    if (argument.Contains("*PI"))
                        limit = Double.Parse(argument.Replace("*PI", "")) * Math.PI;
                    else
                        limit = Double.Parse(argument);

                    var factor = 2 * Math.PI / limit;

                    Parallel.For(0, targetRepresentationLength, x =>
                    {
                        var chunkData = this.GetNaNFreeData(data, x, kernelSize);
                        var length = chunkData.Length;
                        var isHighQuality = (length / (double)kernelSize) >= nanLimit;

                        if (isHighQuality)
                        {
                            for (int i = 0; i < chunkData.Length; i++)
                            {
                                sin[x] += Math.Sin(chunkData[i] * factor);
                                cos[x] += Math.Cos(chunkData[i] * factor);
                            }

                            result[x] = Math.Atan2(sin[x], cos[x]) / factor;

                            if (result[x] < 0)
                                result[x] += limit;
                        }
                        else
                        {
                            result[x] = double.NaN;
                        }
                    });

                    break;

                case AggregationMethod.Min:

                    Parallel.For(0, targetRepresentationLength, x =>
                    {
                        var chunkData = this.GetNaNFreeData(data, x, kernelSize);
                        var isHighQuality = (chunkData.Length / (double)kernelSize) >= nanLimit;

                        if (isHighQuality)
                            result[x] = ArrayStatistics.Minimum(chunkData);
                        else
                            result[x] = double.NaN;
                    });

                    break;

                case AggregationMethod.Max:

                    Parallel.For(0, targetRepresentationLength, x =>
                    {
                        var chunkData = this.GetNaNFreeData(data, x, kernelSize);
                        var isHighQuality = (chunkData.Length / (double)kernelSize) >= nanLimit;

                        if (isHighQuality)
                            result[x] = ArrayStatistics.Maximum(chunkData);
                        else
                            result[x] = double.NaN;
                    });

                    break;

                case AggregationMethod.Std:

                    Parallel.For(0, targetRepresentationLength, x =>
                    {
                        var chunkData = this.GetNaNFreeData(data, x, kernelSize);
                        var isHighQuality = (chunkData.Length / (double)kernelSize) >= nanLimit;

                        if (isHighQuality)
                            result[x] = ArrayStatistics.StandardDeviation(chunkData);
                        else
                            result[x] = double.NaN;
                    });

                    break;

                case AggregationMethod.Rms:

                    Parallel.For(0, targetRepresentationLength, x =>
                    {
                        var chunkData = this.GetNaNFreeData(data, x, kernelSize);
                        var isHighQuality = (chunkData.Length / (double)kernelSize) >= nanLimit;

                        if (isHighQuality)
                            result[x] = ArrayStatistics.RootMeanSquare(chunkData);
                        else
                            result[x] = double.NaN;
                    });

                    break;

                case AggregationMethod.SampleAndHold:

                    Parallel.For(0, targetRepresentationLength, x =>
                    {
                        var chunkData = this.GetNaNFreeData(data, x, kernelSize);
                        var isHighQuality = (chunkData.Length / (double)kernelSize) >= nanLimit;

                        if (isHighQuality)
                            result[x] = chunkData.First();
                        else
                            result[x] = double.NaN;
                    });

                    break;

                case AggregationMethod.Sum:

                    Parallel.For(0, targetRepresentationLength, x =>
                    {
                        var chunkData = this.GetNaNFreeData(data, x, kernelSize);
                        var isHighQuality = (chunkData.Length / (double)kernelSize) >= nanLimit;

                        if (isHighQuality)
                            result[x] = Vector<double>.Build.Dense(chunkData).Sum();
                        else
                            result[x] = double.NaN;
                    });

                    break;

                default:

                    logger.LogWarning($"The aggregation method '{method}' is not known. Skipping period.");

                    break;

            }

            return result;
        }

        internal double[] ApplyAggregationFunction<T>(AggregationMethod method,
                                                      string argument,
                                                      int kernelSize,
                                                      ReadOnlyMemory<T> data,
                                                      ReadOnlyMemory<byte> status,
                                                      double nanLimit,
                                                      ILogger logger) where T : unmanaged
        {
            var targetRepresentationLength = data.Length / kernelSize;
            var result = new double[targetRepresentationLength];

            switch (method)
            {
                case AggregationMethod.MinBitwise:

                    T[] bitField_and = new T[targetRepresentationLength];

                    Parallel.For(0, targetRepresentationLength, x =>
                    {
                        var chunkData = this.GetNaNFreeData(data.Span, status.Span, x, kernelSize);
                        var length = chunkData.Length;
                        var isHighQuality = (length / (double)kernelSize) >= nanLimit;

                        if (isHighQuality)
                        {
                            for (int i = 0; i < length; i++)
                            {
                                if (i == 0)
                                    bitField_and[x] = GenericBitOr<T>.BitOr(bitField_and[x], chunkData[i]);
                                else
                                    bitField_and[x] = GenericBitAnd<T>.BitAnd(bitField_and[x], chunkData[i]);
                            }

                            result[x] = Convert.ToDouble(bitField_and[x]);
                        }
                        else
                        {
                            result[x] = double.NaN;
                        }
                    });

                    break;

                case AggregationMethod.MaxBitwise:

                    T[] bitField_or = new T[targetRepresentationLength];

                    Parallel.For(0, targetRepresentationLength, x =>
                    {
                        var chunkData = this.GetNaNFreeData(data.Span, status.Span, x, kernelSize);
                        var length = chunkData.Length;
                        var isHighQuality = (length / (double)kernelSize) >= nanLimit;

                        if (isHighQuality)
                        {
                            for (int i = 0; i < length; i++)
                            {
                                bitField_or[x] = GenericBitOr<T>.BitOr(bitField_or[x], chunkData[i]);
                            }

                            result[x] = Convert.ToDouble(bitField_or[x]);
                        }
                        else
                        {
                            result[x] = double.NaN;
                        }
                    });

                    break;

                default:
                    logger.LogWarning($"The aggregation method '{method}' is not known. Skipping period.");
                    break;

            }

            return result;
        }

        private unsafe T[] GetNaNFreeData<T>(ReadOnlySpan<T> data, ReadOnlySpan<byte> status, int index, int kernelSize) where T : unmanaged
        {
            var sourceIndex = index * kernelSize;
            var length = data.Length;
            var chunkData = new List<T>();

            for (int i = 0; i < length; i++)
            {
                if (status[sourceIndex + i] == 1)
                    chunkData.Add(data[sourceIndex + i]);
            }

            return chunkData.ToArray();
        }

        private double[] GetNaNFreeData(ReadOnlyMemory<double> data, int index, int kernelSize)
        {
            var sourceIndex = index * kernelSize;

            return MemoryMarshal.ToEnumerable<double>(data)
                .Skip(sourceIndex)
                .Take(kernelSize)
                .Where(value => !double.IsNaN(value))
                .ToArray();
        }

        private static bool ApplyAggregationFilter(Resource resource, Dictionary<AggregationFilter, string> filters, ILogger logger)
        {
            bool result = true;

            // resource
            if (filters.ContainsKey(AggregationFilter.IncludeResource))
                result &= Regex.IsMatch(resource.Id, filters[AggregationFilter.IncludeResource]);

            if (filters.ContainsKey(AggregationFilter.ExcludeResource))
                result &= !Regex.IsMatch(resource.Id, filters[AggregationFilter.ExcludeResource]);

            // group
            var groupNames = resource.Properties
                .Where(entry => entry.Key.StartsWith(DataModelExtensions.Groups))
                .Select(entry => entry.Value.Split(':').Last());

            if (filters.ContainsKey(AggregationFilter.IncludeGroup))
                result &= groupNames
                    .Any(groupName => Regex.IsMatch(groupName, filters[AggregationFilter.IncludeGroup]));

            if (filters.ContainsKey(AggregationFilter.ExcludeGroup))
                result &= !groupNames
                    .Any(groupName => Regex.IsMatch(groupName, filters[AggregationFilter.ExcludeGroup]));

            // unit
            if (filters.ContainsKey(AggregationFilter.IncludeUnit))
            {
                var unit = resource.Properties.GetValueOrDefault(DataModelExtensions.Unit, string.Empty);
                result &= Regex.IsMatch(unit, filters[AggregationFilter.IncludeUnit]);
            }

            if (filters.ContainsKey(AggregationFilter.ExcludeUnit))
            {
                var unit = resource.Properties.GetValueOrDefault(DataModelExtensions.Unit, string.Empty);
                result &= !Regex.IsMatch(unit, filters[AggregationFilter.ExcludeUnit]);
            }

            return result;
        }

        #endregion
    }
}
