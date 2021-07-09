using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Infrastructure;
using Nexus.Utilities;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public class DataSourceController : IDisposable
    {
        #region Constructors

        public DataSourceController(IDataSource dataSource, BackendSource backendSource, ILogger logger)
        {
            this.DataSource = dataSource;
            this.BackendSource = backendSource;
            this.Progress = new Progress<double>();
            this.Logger = logger;
        }

        #endregion

        #region Properties

        public IDataSource DataSource { get; }

        public List<Catalog> Catalogs { get; private set; }

        internal BackendSource BackendSource { get; }

        private Progress<double> Progress { get; }

        private ILogger Logger { get; }

        #endregion

        #region Methods

        public async Task InitializeAsync(DataSourceContext context, CancellationToken cancellationToken)
        {
            await this.DataSource.SetContextAsync(context, cancellationToken);

            if (context.Catalogs is null)
            {
                var catalogs = await this.DataSource.GetCatalogsAsync(cancellationToken);

                catalogs = catalogs
                    .Where(catalog => NexusCoreUtilities.CheckCatalogNamingConvention(catalog.Id, out var _))
                    .ToList();

                foreach (var catalog in catalogs)
                {
                    foreach (var resource in catalog.Resources)
                    {
                        foreach (var dataset in resource.Datasets)
                        {
                            dataset.BackendSource = this.BackendSource;
                        }
                    }
                }

                this.Catalogs = catalogs;
            }
            else
            {
                this.Catalogs = context.Catalogs;
            }
        }

        public DataSourceDoubleStream ReadAsStream(
            DateTime begin,
            DateTime end,
            DatasetRecord datasetRecord)
        {
            var samplePeriod = datasetRecord.Dataset.GetSamplePeriod();
            var elementCount = ExtensibilityUtilities.CalculateElementCount(begin, end, samplePeriod);
            var totalLength = elementCount * NexusCoreUtilities.SizeOf(NexusDataType.FLOAT64);
            var pipe = new Pipe();
            
            _ = this.ReadSingleAsync(
                begin,
                end,
                chunkSize: 1 * 1000 * 1000,
                datasetRecord,
                pipe.Writer,
                statusWriter: default,
                progress: default,
                CancellationToken.None);

            return new DataSourceDoubleStream(totalLength, pipe.Reader);
        }

        public Task ReadSingleAsync(
            DateTime begin,
            DateTime end,
            uint chunkSize,
            DatasetRecord datasetRecord,
            PipeWriter dataWriter,
            PipeWriter? statusWriter,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            /* This (instance) method calls into 
             *  - the general static ReadAsync method which allows reading from more than one data source
             *    and which then calls back into
             *  - the instance method InternalReadAsync which contains the logic to write into the pipes. */

            var samplePeriod = datasetRecord.Dataset.GetSamplePeriod();
            DataSourceController.ValidateParameters(begin, end, samplePeriod);

            var readingGroup = new DataReadingGroup(this, new List<DatasetPipeWriter>() 
            { 
                new DatasetPipeWriter(datasetRecord, dataWriter, statusWriter) 
            });

            return DataSourceController.ReadAsync(
                begin, 
                end, 
                samplePeriod,
                chunkSize,
                new List<DataReadingGroup>() { readingGroup },
                progress,
                cancellationToken);
        }

        public async Task<AvailabilityResult> 
            GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, AvailabilityGranularity granularity, CancellationToken cancellationToken)
        {
            var dateBegin = begin.Date;
            var dateEnd = end.Date;
            var aggregatedData = new ConcurrentDictionary<DateTime, double>();

            switch (granularity)
            {
                case AvailabilityGranularity.Day:

                    var totalDays = (int)(dateEnd - dateBegin).TotalDays;

                    var tasks = Enumerable.Range(0, totalDays).Select(async day =>
                    {
                        var date = dateBegin.AddDays(day);
                        var availability = await this.DataSource.GetAvailabilityAsync(catalogId, date, date.AddDays(1), cancellationToken);
                        aggregatedData.TryAdd(date, availability);
                    });

                    await Task.WhenAll(tasks).ConfigureAwait(false);

                    break;

                case AvailabilityGranularity.Month:

                    var currentBegin = new DateTime(begin.Year, begin.Month, 1, 0, 0, 0, DateTimeKind.Utc);

                    while (currentBegin < end)
                    {
                        var availability = await this.DataSource.GetAvailabilityAsync(catalogId, currentBegin, currentBegin.AddMonths(1), cancellationToken);
                        aggregatedData.TryAdd(currentBegin, availability);
                        currentBegin = currentBegin.AddMonths(1);
                    }

                    break;

                default:
                    throw new NotSupportedException($"Availability granularity value '{granularity}' is not supported.");
            }

            return new AvailabilityResult()
            {
                BackendSource = this.BackendSource,
                Data = aggregatedData.ToDictionary(entry => entry.Key, entry => entry.Value)
            };
        }

        public async Task<TimeRangeResult>
            GetTimeRangeAsync(string catalogId,  CancellationToken cancellationToken)
        {
            (var begin, var end) = await this.DataSource.GetTimeRangeAsync(catalogId, cancellationToken);

            return new TimeRangeResult() 
            {
                BackendSource = this.BackendSource,
                Begin = begin, 
                End = end 
            };
        }

        public async Task<bool> IsDataOfDayAvailableAsync(string catalogId, DateTime day, CancellationToken cancellationToken)
        {
            return (await this.DataSource.GetAvailabilityAsync(catalogId, day, day.AddDays(1), cancellationToken)) > 0;
        }

        public virtual void Dispose()
        {
            //
        }

        private async Task InternalReadAsync(
            DateTime begin, 
            DateTime end,
            TimeSpan samplePeriod,
            List<DatasetPipeWriter> datasetPipeWriters,
            CancellationToken cancellationToken)
        {
            var index = 1;
            var count = datasetPipeWriters.Count;
            var elementCount = ExtensibilityUtilities.CalculateElementCount(begin, end, samplePeriod);

            var requests = datasetPipeWriters.Select(datasetPipeWriter =>
            {
                var (datasetRecord, dataWriter, statusWriter) = datasetPipeWriter;
                Memory<byte> data;
                Memory<byte> status;

                if (statusWriter is null)
                {
                    /* sizes */
                    var elementSize = sizeof(double);
                    var dataLength = elementCount * elementSize;

                    /* data memory */
                    using var dataOwner = MemoryPool<byte>.Shared.Rent(dataLength);
                    var dataMemory = dataOwner.Memory.Slice(dataLength);
                    dataMemory.Span.Clear();

                    /* status memory */
                    using var statusOwner = MemoryPool<byte>.Shared.Rent(elementCount);
                    var statusMemory = statusOwner.Memory.Slice(elementCount);
                    statusMemory.Span.Clear();

                    /* get data */
                    data = dataMemory;
                    status = statusMemory;
                }
                else
                {
                    /* sizes */
                    var elementSize = datasetRecord.Dataset.ElementSize;
                    var dataLength = elementCount * elementSize;

                    /* data memory */
                    var dataMemory = dataWriter
                        .GetMemory(dataLength)
                        .Slice(0, dataLength);

                    dataMemory.Span.Clear(); // I think this is required, but found no clear evidence in the docs.

                    /* status memory */
                    var statusMemory = statusWriter
                        .GetMemory(elementCount)
                        .Slice(0, elementCount);

                    statusMemory.Span.Clear(); // I think this is required, but found no clear evidence in the docs.

                    /* get data */
                    data = dataMemory;
                    status = statusMemory;
                }

                return new ReadRequest(datasetRecord.GetPath(), data, status);
            }).ToArray();

            await this.DataSource.ReadAsync(
                    begin,
                    end,
                    requests,
                    this.Progress,
                    cancellationToken);

            foreach (var (datasetPipeWriter, readRequest) in datasetPipeWriters.Zip(requests))
            {
                var (datasetRecord, dataWriter, statusWriter) = datasetPipeWriter;

                if (statusWriter is null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    /* sizes */
                    var elementSize = sizeof(double);
                    var dataLength = elementCount * elementSize;

                    /* apply status */
                    var buffer = dataWriter
                        .GetMemory(dataLength)
                        .Slice(0, dataLength);

                    BufferUtilities.ApplyDatasetStatusByDataType(
                        datasetRecord.Dataset.DataType,
                        readRequest.Data,
                        readRequest.Status,
                        target: new CastMemoryManager<byte, double>(buffer).Memory);

                    /* update progress */
                    dataWriter.Advance(dataLength);
                    await dataWriter.FlushAsync();
                }
                else
                {
                    /* sizes */
                    var elementSize = datasetRecord.Dataset.ElementSize;
                    var dataLength = elementCount * elementSize;

                    /* update progress */
                    dataWriter.Advance(dataLength);
                    await dataWriter.FlushAsync();

                    statusWriter.Advance(elementCount);
                    await statusWriter.FlushAsync();
                }
            }
        }

        #endregion

        #region Static Methods

        public static async Task ReadAsync(
            DateTime begin,
            DateTime end,
            TimeSpan samplePeriod,
            uint chunkSize,
            List<DataReadingGroup> readingGroups,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            /* validation */
            foreach (var datasetPipeWriters in readingGroups.SelectMany(readingGroup => readingGroup.DatasetPipeWriters))
            {
                if (datasetPipeWriters.DatasetRecord.Dataset.GetSamplePeriod() != samplePeriod)
                    throw new ValidationException("All datasets must be of the same sample period.");
            }

            DataSourceController.ValidateParameters(begin, end, samplePeriod);

            /* pre-calculation */
            var bytesPerRow = readingGroups
                .SelectMany(readingGroup => readingGroup.DatasetPipeWriters)
                .Sum(datasetPipeWriter => datasetPipeWriter.DatasetRecord.Dataset.ElementSize);

            TimeSpan maxPeriodPerRequest;

            if (chunkSize > 0)
            {
                var rows = chunkSize / bytesPerRow;

                if (rows == 0)
                    throw new ValidationException("The chunk size size is smaller than the expected row size.");

                maxPeriodPerRequest = TimeSpan.FromTicks(samplePeriod.Ticks * rows);
            }
            else
            {
                maxPeriodPerRequest = TimeSpan.MaxValue;
            }

            /* periods */
            var totalPeriod = end - begin;
            var consumedPeriod = TimeSpan.Zero;
            var remainingPeriod = totalPeriod;
            var currentPeriod = default(TimeSpan);

            /* progress */
            var currentDataSourceProgress = new ConcurrentDictionary<DataSourceController, double>();

            foreach (var (controller, datasetPipeWriters) in readingGroups)
            {
                /* no need to remove handler because of short lifetime of IDataSource */
                controller.Progress.ProgressChanged += (sender, progressValue) =>
                {
                    if (progressValue <= 1)
                    {
                        currentDataSourceProgress.AddOrUpdate(controller, progressValue, (_, _) => progressValue);

                        // https://stackoverflow.com/a/62768272
                        var baseProgress = consumedPeriod.Ticks / (double)totalPeriod.Ticks;
                        var relativeProgressFactor = currentPeriod.Ticks / (double)totalPeriod.Ticks;
                        var relativeProgress = currentDataSourceProgress.Sum(entry => entry.Value) * relativeProgressFactor;

                        progress?.Report(baseProgress + relativeProgress);
                    }
                };
            }

            /* go */
            while (consumedPeriod < totalPeriod)
            {
                currentDataSourceProgress.Clear();
                currentPeriod = TimeSpan.FromTicks(Math.Min(remainingPeriod.Ticks, maxPeriodPerRequest.Ticks));

                var readingTasks = readingGroups.Select(async readingGroup =>
                {
                    var (controller, datasetPipeWriters) = readingGroup;
                    var currentBegin = begin + consumedPeriod;
                    var currentEnd = begin + currentPeriod;

                    try
                    {
                        await controller.InternalReadAsync(
                            currentBegin,
                            currentEnd,
                            samplePeriod,
                            datasetPipeWriters,
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        controller.Logger.LogWarning(ex.GetFullMessage());
                    }
                });

                await Task.WhenAll(readingTasks).ConfigureAwait(false);

                /* continue in time */
                consumedPeriod += currentPeriod;
                remainingPeriod -= currentPeriod;

                progress?.Report(consumedPeriod.Ticks / (double)totalPeriod.Ticks);
            }

            /* complete */
            foreach (var readingGroup in readingGroups)
            {
                foreach (var datasetPipeWriter in readingGroup.DatasetPipeWriters)
                {
                    await datasetPipeWriter.DataWriter.CompleteAsync().ConfigureAwait(false);

                    if (datasetPipeWriter.StatusWriter is not null)
                        await datasetPipeWriter.StatusWriter.CompleteAsync().ConfigureAwait(false);
                }
            }
        }

        private static void ValidateParameters(DateTime begin, DateTime end, TimeSpan samplePeriod)
        {
            /* When the user requests two time series of the same frequency, they will be aligned to the sample
             * period. With the current implementation, it simply not possible for one data source to provide an 
             * offset which is smaller than the sample period. In future a solution could be to have time series 
             * data with associated time stamps, which is not yet implemented.
             */

            if (begin >= end)
                throw new ValidationException("The begin datetime must be less than the end datetime.");

            if (begin.Ticks % samplePeriod.Ticks != 0)
                throw new ValidationException("The begin parameter must be a multiple of the sample period.");

            if (end.Ticks % samplePeriod.Ticks != 0)
                throw new ValidationException("The end parameter must be a multiple of the sample period.");
        }

        #endregion
    }
}
