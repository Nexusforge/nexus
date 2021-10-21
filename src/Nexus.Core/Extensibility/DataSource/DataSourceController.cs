using Microsoft.Extensions.Logging;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Utilities;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    internal class DataSourceController : IDataSourceController
    {
        #region Fields

        public ResourceCatalog[] _catalogs;

        #endregion

        #region Constructors

        public DataSourceController(IDataSource dataSource, BackendSource backendSource, ILogger logger)
        {
            this.DataSource = dataSource;
            this.BackendSource = backendSource;
            this.Logger = logger;
        }

        #endregion

        #region Properties

        internal static uint ChunkSize { get; set; } = NexusConstants.DefaultChunkSize;

        internal IDataSource DataSource { get; }

        private BackendSource BackendSource { get; }

        private ILogger Logger { get; }

        #endregion

        #region Methods

        public async Task InitializeAsync(ResourceCatalog[]? catalogs, CancellationToken cancellationToken)
        {
            _catalogs = catalogs;

            var context = new DataSourceContext()
            {
                ResourceLocator = this.BackendSource.ResourceLocator,
                Configuration = this.BackendSource.Configuration,
                Logger = this.Logger,
                Catalogs = catalogs
            };

            await this.DataSource.SetContextAsync(context, cancellationToken);
        }

        public async Task<ResourceCatalog[]>
           GetCatalogsAsync(CancellationToken cancellationToken)
        {
            if (_catalogs is null)
            {
                var catalogs = await this.DataSource.GetCatalogsAsync(cancellationToken);

                foreach (var catalog in catalogs)
                {
                    foreach (var resource in catalog.Resources)
                    {
                        foreach (var representation in resource.Representations)
                        {
                            representation.BackendSource = this.BackendSource;
                        }
                    }
                }

                _catalogs = catalogs;
            }

            return _catalogs;
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

                    await Task.WhenAll(tasks);

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

            return new AvailabilityResult(
                BackendSource: this.BackendSource,
                Data: aggregatedData.ToDictionary(entry => entry.Key, entry => entry.Value));
        }

        public async Task<TimeRangeResult>
            GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken)
        {
            (var begin, var end) = await this.DataSource.GetTimeRangeAsync(catalogId, cancellationToken);

            return new TimeRangeResult(
                BackendSource: this.BackendSource,
                Begin: begin,
                End: end);
        }

        public async Task<bool> IsDataOfDayAvailableAsync(string catalogId, DateTime day, CancellationToken cancellationToken)
        {
            return (await this.DataSource.GetAvailabilityAsync(catalogId, day, day.AddDays(1), cancellationToken)) > 0;
        }

        public async Task ReadAsync(
            DateTime begin,
            DateTime end,
            TimeSpan samplePeriod,
            CatalogItemPipeWriter[] catalogItemPipeWriters,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            var count = catalogItemPipeWriters.Length;
            var elementCount = ExtensibilityUtilities.CalculateElementCount(begin, end, samplePeriod);
            var memoryOwners = new List<IMemoryOwner<byte>>();

            var catalogs = await this.GetCatalogsAsync(cancellationToken);

            var requests = catalogItemPipeWriters.Select(catalogItemPipeWriter =>
            {
                var (catalogItem, dataWriter, statusWriter) = catalogItemPipeWriter;
                Memory<byte> data;
                Memory<byte> status;

                if (statusWriter is null)
                {
                    /* sizes */
                    var elementSize = sizeof(double);
                    var dataLength = elementCount * elementSize;

                    /* data memory */
                    var dataOwner = MemoryPool<byte>.Shared.Rent(dataLength);
                    var dataMemory = dataOwner.Memory.Slice(0, dataLength);
                    dataMemory.Span.Clear();
                    memoryOwners.Add(dataOwner);

                    /* status memory */
                    var statusOwner = MemoryPool<byte>.Shared.Rent(elementCount);
                    var statusMemory = statusOwner.Memory.Slice(0, elementCount);
                    memoryOwners.Add(statusOwner);
                    statusMemory.Span.Clear();

                    /* get data */
                    data = dataMemory;
                    status = statusMemory;
                }
                else
                {
                    /* sizes */
                    var elementSize = catalogItem.Representation.ElementSize;
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

                var originalCatalogItem = catalogs.Find(catalogItem.GetPath());
                return new ReadRequest(originalCatalogItem, data, status);
            }).ToArray();

            try
            {
                await this.DataSource.ReadAsync(
                    begin,
                    end,
                    requests,
                    progress,
                    cancellationToken);

                foreach (var (catalogItemPipeWriter, readRequest) in catalogItemPipeWriters.Zip(requests))
                {
                    var (catalogItem, dataWriter, statusWriter) = catalogItemPipeWriter;

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

                        BufferUtilities.ApplyRepresentationStatusByDataType(
                            catalogItem.Representation.DataType,
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
                        var elementSize = catalogItem.Representation.ElementSize;
                        var dataLength = elementCount * elementSize;

                        /* update progress */
                        dataWriter.Advance(dataLength);
                        await dataWriter.FlushAsync();

                        statusWriter.Advance(elementCount);
                        await statusWriter.FlushAsync();
                    }
                }
            }
            finally
            {
                foreach (var memoryOwner in memoryOwners)
                {
                    memoryOwner.Dispose();
                }
            }
        }

        #endregion

        #region Static Methods

        public static async Task ReadAsync(
            DateTime begin,
            DateTime end,
            TimeSpan samplePeriod,
            DataReadingGroup[] readingGroups,
            IProgress<double>? progress,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            /* validation */
            foreach (var catalogItemPipeWriters in readingGroups.SelectMany(readingGroup => readingGroup.CatalogItemPipeWriters))
            {
                if (catalogItemPipeWriters.CatalogItem.Representation.SamplePeriod != samplePeriod)
                    throw new ValidationException("All representations must be of the same sample period.");
            }

            DataSourceController.ValidateParameters(begin, end, samplePeriod);

            /* pre-calculation */
            var bytesPerRow = readingGroups
                .SelectMany(readingGroup => readingGroup.CatalogItemPipeWriters)
                .Sum(catalogItemPipeWriter => catalogItemPipeWriter.CatalogItem.Representation.ElementSize);

            var chunkSize = Math.Max(bytesPerRow, DataSourceController.ChunkSize);
            var rows = chunkSize / bytesPerRow;
            var maxPeriodPerRequest = TimeSpan.FromTicks(samplePeriod.Ticks * rows);

            /* periods */
            var totalPeriod = end - begin;
            var consumedPeriod = TimeSpan.Zero;
            var remainingPeriod = totalPeriod;
            var currentPeriod = default(TimeSpan);

            /* progress */
            var currentDataSourceProgress = new ConcurrentDictionary<IDataSourceController, double>();

            /* go */
            while (consumedPeriod < totalPeriod)
            {
                currentDataSourceProgress.Clear();
                currentPeriod = TimeSpan.FromTicks(Math.Min(remainingPeriod.Ticks, maxPeriodPerRequest.Ticks));

                var readingTasks = readingGroups.Select(async readingGroup =>
                {
                    var (controller, catalogItemPipeWriters) = readingGroup;
                    var currentBegin = begin + consumedPeriod;
                    var currentEnd = currentBegin + currentPeriod;

                    try
                    {
                        /* no need to remove handler because of short lifetime of IDataSource */
                        var dataSourceProgress = new Progress<double>();

                        dataSourceProgress.ProgressChanged += (sender, progressValue) =>
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

                        await controller.ReadAsync(
                            currentBegin,
                            currentEnd,
                            samplePeriod,
                            catalogItemPipeWriters,
                            dataSourceProgress,
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex.GetFullMessage());
                    }
                });

                await Task.WhenAll(readingTasks);

                /* continue in time */
                consumedPeriod += currentPeriod;
                remainingPeriod -= currentPeriod;

                progress?.Report(consumedPeriod.Ticks / (double)totalPeriod.Ticks);
            }

            /* complete */
            foreach (var readingGroup in readingGroups)
            {
                foreach (var catalogItemPipeWriter in readingGroup.CatalogItemPipeWriters)
                {
                    await catalogItemPipeWriter.DataWriter.CompleteAsync();

                    if (catalogItemPipeWriter.StatusWriter is not null)
                        await catalogItemPipeWriter.StatusWriter.CompleteAsync();
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

        #region IDisposable

        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    var disposable = this.DataSource as IDisposable;
                    disposable?.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
