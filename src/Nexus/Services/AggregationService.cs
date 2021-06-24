using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Statistics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Nexus.Buffers;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static Nexus.Services.DatabaseManager;

namespace Nexus.Services
{
    public class AggregationService
    {
        #region Fields

        private ILogger _logger;

        private IFileAccessManager _fileAccessManager;

        #endregion

        #region Constructors

        public AggregationService(
            IFileAccessManager fileAccessManager,
            ILogger<AggregationService> logger)
        {
            _fileAccessManager = fileAccessManager;
            _logger = logger;

            this.Progress = new Progress<ProgressUpdatedEventArgs>();
        }

        #endregion

        #region Properties

        public Progress<ProgressUpdatedEventArgs> Progress { get; }

        #endregion

        #region Methods

        public static List<AggregationInstruction> ComputeInstructions(AggregationSetup setup, DatabaseManagerState state, ILogger logger)
        {
            var catalogIds = setup.Aggregations
                .Select(aggregation => aggregation.CatalogId)
                .Distinct().ToList();

            return catalogIds.Select(catalogId =>
            {
                var container = state.Database.CatalogContainers.FirstOrDefault(container => container.Id == catalogId);

                if (container is null)
                    return null;

                var backendSources = container
                    .Catalog
                    .Channels
                    .SelectMany(channel => channel.Datasets.Select(dataset => dataset.BackendSource))
                    .Distinct()
                    .Where(backendSource => backendSource != state.AggregationBackendSource)
                    .ToList();

                return new AggregationInstruction(container, backendSources.ToDictionary(backendSource => backendSource, backendSource =>
                {
                    // find aggregations for catalog ID
                    var potentialAggregations = setup.Aggregations
                        .Where(parameters => parameters.CatalogId == container.Catalog.Id)
                        .ToList();

                    // create channel to aggregations map
                    var aggregationChannels = container.Catalog.Channels
                        // find all channels for current reader backend source
                        .Where(channel => channel.Datasets.Any(dataset => dataset.BackendSource == backendSource))
                        // find all aggregations for current channel
                        .Select(channel =>
                        {
                            var channelMeta = container.CatalogSettings.Channels
                                .First(current => current.Id == channel.Id);

                            return new AggregationChannel()
                            {
                                Channel = channel,
                                Aggregations = potentialAggregations.Where(current => AggregationService.ApplyAggregationFilter(channel, channelMeta, current.Filters, logger)).ToList()
                            };
                        })
                        // take all channels with aggregations
                        .Where(aggregationChannel => aggregationChannel.Aggregations.Any());

                    return aggregationChannels.ToList();
                }));
            }).Where(instruction => instruction != null).ToList();
        }

        public Task<string> AggregateDataAsync(string databaseFolderPath,
                                               uint aggregationChunkSizeMB,
                                               AggregationSetup setup,
                                               DatabaseManagerState state,
                                               Func<BackendSource, Task<DataSourceController>> getControllerAsync,
                                               CancellationToken cancellationToken)
        {
            if (setup.Begin != setup.Begin.Date)
                throw new ValidationException("The begin parameter must have no time component.");

            if (setup.End != setup.End.Date)
                throw new ValidationException("The end parameter must have no time component.");

            return Task.Run(async () =>
            {
                var progress = (IProgress<ProgressUpdatedEventArgs>)this.Progress;
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
                        var eventArgs = new ProgressUpdatedEventArgs(progressValue, progressMessage);
                        progress.Report(eventArgs);

                        await this.AggregateCatalogAsync(
                            databaseFolderPath,
                            catalogId,
                            aggregationChunkSizeMB,
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
                                                 uint aggregationChunkSizeMB,
                                                 DateTime date,
                                                 DatabaseManagerState state,
                                                 AggregationSetup setup,
                                                 AggregationInstruction instruction,
                                                 Func<BackendSource, Task<DataSourceController>> getControllerAsync,
                                                 CancellationToken cancellationToken)
        {
            foreach (var (backendSource, aggregationChannels) in instruction.DataReaderToAggregationsMap)
            {
                using var controller = await getControllerAsync(backendSource);

                // get files
                if (!await controller.IsDataOfDayAvailableAsync(catalogId, date, cancellationToken))
                    return;

                // catalog
                var container = state.Database.CatalogContainers.FirstOrDefault(container => container.Id == catalogId);

                if (container == null)
                    throw new Exception($"The requested catalog '{catalogId}' could not be found.");

                var targetDirectoryPath = Path.Combine(databaseFolderPath, "DATA", WebUtility.UrlEncode(container.Id), date.ToString("yyyy-MM"), date.ToString("dd"));

                // for each channel
                foreach (var aggregationChannel in aggregationChannels)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var dataset = aggregationChannel.Channel.Datasets.First();

                        await (Task)NexusUtilities.InvokeGenericMethod(this, nameof(this.OrchestrateAggregationAsync),
                                                            BindingFlags.Instance | BindingFlags.NonPublic,
                                                            NexusUtilities.GetTypeFromNexusDataType(dataset.DataType),
                                                            new object[] 
                                                            {
                                                                targetDirectoryPath,
                                                                controller, 
                                                                dataset, 
                                                                aggregationChannel.Aggregations, 
                                                                date,
                                                                aggregationChunkSizeMB,
                                                                setup.Force, 
                                                                cancellationToken
                                                            });
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
                                                           DataSourceController dataReader,
                                                           Dataset dataset,
                                                           List<Aggregation> aggregations,
                                                           DateTime date,
                                                           uint aggregationChunkSizeMB,
                                                           bool force,
                                                           CancellationToken cancellationToken) where T : unmanaged
        {
            // check source sample rate
            var _ = new SampleRateContainer(dataset.Id, ensureNonZeroIntegerHz: true);

            // prepare variables
            var units = new List<AggregationUnit>();
            var channel = dataset.Channel;

            // prepare buffers
            foreach (var aggregation in aggregations)
            {
                var periodsToSkip = new List<int>();

                foreach (var period in aggregation.Periods)
                {
#warning Ensure that period is a sensible value

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

                        var targetFileName = $"{channel.Id}_{period}_s_{methodIdentifier}.nex";
                        var targetFilePath = Path.Combine(targetDirectoryPath, targetFileName);

                        if (force || !File.Exists(targetFilePath))
                        {
                            var buffer = new double[86400 / period];

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
            var fundamentalPeriod = TimeSpan.FromMinutes(10); // required to ensure that the aggregation functions get data with a multiple length of 10 minutes
            var endDate = date.AddDays(1);
            var blockSizeLimit = aggregationChunkSizeMB * 1000 * 1000;

            // read raw data
            await foreach (var progressRecord in dataReader.ReadAsync(dataset, date, endDate, blockSizeLimit, fundamentalPeriod, cancellationToken))
            {
                var result = progressRecord.DatasetToResultMap.First().Value;

                // aggregate data
                var data = result.GetData<T>();
                var partialBuffersMap = this.ApplyAggregationFunction(dataset, data, result.Status, units);

                foreach (var entry in partialBuffersMap)
                {
                    // copy aggregated data to target buffer
                    var partialBuffer = entry.Value;
                    var unit = entry.Key;

                    Array.Copy(partialBuffer, 0, unit.Buffer, unit.BufferPosition, partialBuffer.Length);
                    unit.BufferPosition += partialBuffer.Length;
                }
            }

            // write data to file
            foreach (var unit in units)
            {
                try
                {
                    _fileAccessManager.Register(unit.TargetFilePath, cancellationToken);

                    if (File.Exists(unit.TargetFilePath))
                        File.Delete(unit.TargetFilePath);

                    // create data file
                    AggregationFile.Create<double>(unit.TargetFilePath, unit.Buffer);
                }
                finally
                {
                    _fileAccessManager.Unregister(unit.TargetFilePath);
                }
            }
        }

        private Dictionary<AggregationUnit, double[]> ApplyAggregationFunction<T>(Dataset dataset,
                                                                                  Memory<T> data,
                                                                                  Memory<byte> status,
                                                                                  List<AggregationUnit> aggregationUnits) where T : unmanaged
        {
            var nanLimit = 0.99;
            var dataset_double = default(double[]);
            var partialBuffersMap = new Dictionary<AggregationUnit, double[]>();

            foreach (var unit in aggregationUnits)
            {
                var aggregation = unit.Aggregation;
                var period = unit.Period;
                var method = unit.Method;
                var argument = unit.Argument;
                var sampleCount = dataset.GetSampleRate(ensureNonZeroIntegerHz: true).SamplesPerSecondAsUInt64 * (ulong)period;

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

                        if (dataset_double == null)
                            dataset_double = BufferUtilities.ApplyDatasetStatus(data, status);

                        partialBuffersMap[unit] = this.ApplyAggregationFunction(method, argument, (int)sampleCount, dataset_double, nanLimit, _logger);

                        break;

                    case AggregationMethod.MinBitwise:
                    case AggregationMethod.MaxBitwise:

                        partialBuffersMap[unit] = this.ApplyAggregationFunction(method, argument, (int)sampleCount, data, status, nanLimit, _logger);

                        break;

                    default:

                        _logger.LogWarning($"The aggregation method '{unit.Method}' is not known. Skipping period {period}.");

                        continue;
                }
            }

            return partialBuffersMap;
        }

        internal double[] ApplyAggregationFunction(AggregationMethod method,
                                                   string argument,
                                                   int kernelSize,
                                                   Memory<double> data,
                                                   double nanLimit,
                                                   ILogger logger)
        {
            var targetDatasetLength = data.Length / kernelSize;
            var result = new double[targetDatasetLength];

            switch (method)
            {
                case AggregationMethod.Mean:

                    Parallel.For(0, targetDatasetLength, x =>
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

                    double[] sin = new double[targetDatasetLength];
                    double[] cos = new double[targetDatasetLength];
                    double limit;

                    if (argument.Contains("*PI"))
                        limit = Double.Parse(argument.Replace("*PI", "")) * Math.PI;
                    else
                        limit = Double.Parse(argument);

                    var factor = 2 * Math.PI / limit;

                    Parallel.For(0, targetDatasetLength, x =>
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

                    Parallel.For(0, targetDatasetLength, x =>
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

                    Parallel.For(0, targetDatasetLength, x =>
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

                    Parallel.For(0, targetDatasetLength, x =>
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

                    Parallel.For(0, targetDatasetLength, x =>
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

                    Parallel.For(0, targetDatasetLength, x =>
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

                    Parallel.For(0, targetDatasetLength, x =>
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
                                                      Memory<T> data,
                                                      Memory<byte> status,
                                                      double nanLimit,
                                                      ILogger logger) where T : unmanaged
        {
            var targetDatasetLength = data.Length / kernelSize;
            var result = new double[targetDatasetLength];

            switch (method)
            {
                case AggregationMethod.MinBitwise:

                    T[] bitField_and = new T[targetDatasetLength];

                    Parallel.For(0, targetDatasetLength, x =>
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

                    T[] bitField_or = new T[targetDatasetLength];

                    Parallel.For(0, targetDatasetLength, x =>
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

        private unsafe T[] GetNaNFreeData<T>(Span<T> data, Span<byte> status, int index, int kernelSize) where T : unmanaged
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

        private double[] GetNaNFreeData(Memory<double> data, int index, int kernelSize)
        {
            var sourceIndex = index * kernelSize;

            return MemoryMarshal.ToEnumerable<double>(data)
                .Skip(sourceIndex)
                .Take(kernelSize)
                .Where(value => !double.IsNaN(value))
                .ToArray();
        }

        private static bool ApplyAggregationFilter(Channel channel, ChannelMeta channelMeta, Dictionary<AggregationFilter, string> filters, ILogger logger)
        {
            bool result = true;

            // channel
            if (filters.ContainsKey(AggregationFilter.IncludeChannel))
                result &= Regex.IsMatch(channel.Name, filters[AggregationFilter.IncludeChannel]);

            if (filters.ContainsKey(AggregationFilter.ExcludeChannel))
                result &= !Regex.IsMatch(channel.Name, filters[AggregationFilter.ExcludeChannel]);

            // group
            if (filters.ContainsKey(AggregationFilter.IncludeGroup))
                result &= channel.Group.Split('\n').Any(groupName => Regex.IsMatch(groupName, filters[AggregationFilter.IncludeGroup]));

            if (filters.ContainsKey(AggregationFilter.ExcludeGroup))
                result &= !channel.Group.Split('\n').Any(groupName => Regex.IsMatch(groupName, filters[AggregationFilter.ExcludeGroup]));

            // unit
            if (filters.ContainsKey(AggregationFilter.IncludeUnit))
            {
#warning Remove this special case check.
                if (channel.Unit == null)
                {
                    logger.LogWarning("Unit 'null' value detected.");
                    result &= false;
                }
                else
                {
                    var unit = !string.IsNullOrWhiteSpace(channelMeta.Unit)
                        ? channelMeta.Unit
                        : channel.Unit;

                    result &= Regex.IsMatch(unit, filters[AggregationFilter.IncludeUnit]);
                }
            }

            if (filters.ContainsKey(AggregationFilter.ExcludeUnit))
            {
#warning Remove this special case check.
                if (channel.Unit == null)
                {
                    logger.LogWarning("Unit 'null' value detected.");
                    result &= true;

                }
                else
                {
                    var unit = !string.IsNullOrWhiteSpace(channelMeta.Unit)
                        ? channelMeta.Unit
                        : channel.Unit;

                    result &= !Regex.IsMatch(unit, filters[AggregationFilter.ExcludeUnit]);
                }
            }

            return result;
        }

        #endregion
    }
}
