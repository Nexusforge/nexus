using Nexus.Core;
using Nexus.DataModel;
using Nexus.Utilities;
using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    internal class DataSourceController : IDataSourceController
    {
        #region Fields

        public ConcurrentDictionary<string, ResourceCatalog> _catalogCache = null!;

        #endregion

        #region Constructors

        public DataSourceController(
            IDataSource dataSource, 
            DataSourceRegistration registration,
            IReadOnlyDictionary<string, string> userConfiguration,
            ILogger<DataSourceController> logger)
        {
            DataSource = dataSource;
            DataSourceRegistration = registration;
            UserConfiguration = userConfiguration;
            Logger = logger;
        }

        #endregion

        #region Properties

        private IDataSource DataSource { get; }

        private DataSourceRegistration DataSourceRegistration { get; }

        internal IReadOnlyDictionary<string, string> UserConfiguration { get; }

        private ILogger Logger { get; }

        #endregion

        #region Methods

        public async Task InitializeAsync(ConcurrentDictionary<string, ResourceCatalog> catalogCache, ILogger logger, CancellationToken cancellationToken)
        {
            _catalogCache = catalogCache;

            var mergedConfiguration = DataSourceRegistration.Configuration
                .ToDictionary(entry => entry.Key, entry => entry.Value);

            foreach (var entry in UserConfiguration)
            {
                mergedConfiguration[entry.Key] = entry.Value;
            }

            var context = new DataSourceContext(
                ResourceLocator: DataSourceRegistration.ResourceLocator,
                Configuration: mergedConfiguration,
                Logger: logger);

            await DataSource.SetContextAsync(context, cancellationToken);
        }

        public async Task<CatalogRegistration[]>
           GetCatalogRegistrationsAsync(string path, CancellationToken cancellationToken)
        {
            var catalogRegistrations = await DataSource
                .GetCatalogRegistrationsAsync(path, cancellationToken);

            for (int i = 0; i < catalogRegistrations.Length; i++)
            {
                // absolute
                if (catalogRegistrations[i].Path.StartsWith('/'))
                {
                    if (!catalogRegistrations[i].Path.StartsWith(path))
                        throw new Exception($"The catalog path {catalogRegistrations[i].Path} is not a sub path of {path}.");
                }
                // relative
                else
                {
                    catalogRegistrations[i] = catalogRegistrations[i] with
                    { 
                        Path = path + catalogRegistrations[i].Path 
                    };
                }
            }

            if (catalogRegistrations.Any(catalogRegistration => !catalogRegistration.Path.StartsWith(path)))
                throw new Exception($"The returned catalog identifier is not a child of {path}.");

            return catalogRegistrations;
        }

        public async Task<ResourceCatalog>
            GetCatalogAsync(string catalogId, CancellationToken cancellationToken)
        {
            Logger.LogDebug("Load catalog {CatalogId}", catalogId);

            var catalog = await DataSource.GetCatalogAsync(catalogId, cancellationToken);

            /* GetOrAdd is not working because it requires a synchronous delegate */
            _catalogCache.TryAdd(catalogId, catalog);

            return catalog;
        }

        public async Task<CatalogAvailability>
            GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            var dateBegin = begin.Date;
            var dateEnd = end.Date;
            var aggregatedData = new ConcurrentDictionary<DateTime, double>();
            var totalDays = (int)(dateEnd - dateBegin).TotalDays;

            var tasks = Enumerable.Range(0, totalDays).Select(async day =>
            {
                var date = dateBegin.AddDays(day);
                var availability = await DataSource.GetAvailabilityAsync(catalogId, date, date.AddDays(1), cancellationToken);
                aggregatedData.TryAdd(date, availability);
            });

            await Task.WhenAll(tasks);

            return new CatalogAvailability(
                Data: aggregatedData.ToDictionary(entry => entry.Key, entry => entry.Value));
        }

        public async Task<CatalogTimeRange>
            GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken)
        {
            (var begin, var end) = await DataSource.GetTimeRangeAsync(catalogId, cancellationToken);

            return new CatalogTimeRange(
                Begin: begin,
                End: end);
        }

        public async Task<bool> IsDataOfDayAvailableAsync(string catalogId, DateTime day, CancellationToken cancellationToken)
        {
            return (await DataSource.GetAvailabilityAsync(catalogId, day, day.AddDays(1), cancellationToken)) > 0;
        }

        public async Task ReadAsync(
            DateTime begin,
            DateTime end,
            TimeSpan samplePeriod,
            CatalogItemRequestPipeWriter[] catalogItemRequestPipeWriters,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            var count = catalogItemRequestPipeWriters.Length;
            var elementCount = ExtensibilityUtilities.CalculateElementCount(begin, end, samplePeriod);
            var memoryOwners = new List<IMemoryOwner<byte>>();

            /* prepare requests variable */
            var requests = catalogItemRequestPipeWriters.Select(catalogItemRequestPipeWriter =>
            {
                var (catalogItemRequest, dataWriter) = catalogItemRequestPipeWriter;
                var catalogItem = catalogItemRequest.Item;

                Memory<byte> data;
                Memory<byte> status;

                /* sizes */
                var elementSize = catalogItem.Representation.ElementSize;
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

                /* _catalogMap is guaranteed to contain the current catalog 
                 * because GetCatalogAsync is called before ReadAsync */
                if (_catalogCache.TryGetValue(catalogItem.Catalog.Id, out var catalog))
                {
                    var originalCatalogItem = catalog.Find(catalogItem.ToPath());
                    return new ReadRequest(originalCatalogItem, data, status);
                }

                else
                {
                    throw new Exception($"Cannot find cataog {catalogItem.Catalog.Id}.");
                }
                
            }).ToArray();

            try
            {
                await DataSource.ReadAsync(
                    begin,
                    end,
                    requests,
                    progress,
                    cancellationToken);

                var dataTasks = new List<ValueTask<FlushResult>>(capacity: catalogItemRequestPipeWriters.Length);
                var statusTasks = new List<ValueTask<FlushResult>>(capacity: catalogItemRequestPipeWriters.Length);

                /* start all tasks */
                foreach (var (catalogItemRequestPipeWriter, readRequest) in catalogItemRequestPipeWriters.Zip(requests))
                {
                    var (catalogItemRequest, dataWriter) = catalogItemRequestPipeWriter;

                    using var scope = Logger.BeginScope(new Dictionary<string, object>()
                    {
                        ["ResourcePath"] = catalogItemRequest.Item.ToPath()
                    });

                    cancellationToken.ThrowIfCancellationRequested();

                    /* sizes */
                    var elementSize = sizeof(double);
                    var dataLength = elementCount * elementSize;

                    /* apply status */
                    var buffer = dataWriter
                        .GetMemory(dataLength)
                        .Slice(0, dataLength);

                    Logger.LogTrace("Merge status buffer and data buffer");

#warning this is blocking
                    BufferUtilities.ApplyRepresentationStatusByDataType(
                        catalogItemRequest.Item.Representation.DataType,
                        readRequest.Data,
                        readRequest.Status,
                        target: new CastMemoryManager<byte, double>(buffer).Memory);

                    /* update progress */
                    Logger.LogTrace("Advance data pipe writer by {DataLength} bytes", dataLength);
                    dataWriter.Advance(dataLength);
                    dataTasks.Add(dataWriter.FlushAsync());
                }

                /* wait for tasks to finish */
                await NexusCoreUtilities.WhenAll(dataTasks.ToArray());
                await NexusCoreUtilities.WhenAll(statusTasks.ToArray());
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
            GeneralOptions generalOptions,
            IProgress<double>? progress,
            ILogger<DataSourceController> logger,
            CancellationToken cancellationToken)
        {
            /* validation */
            var catalogItemRequestPipeWriters = readingGroups.SelectMany(readingGroup => readingGroup.CatalogItemRequestPipeWriters);

            if (!catalogItemRequestPipeWriters.Any())
                return;

            foreach (var catalogItemRequestPipeWriter in catalogItemRequestPipeWriters)
            {
                var currentSamplePeriod = catalogItemRequestPipeWriter.Request.Item.Representation.SamplePeriod;

                if (currentSamplePeriod < samplePeriod)
//#error
                    if (catalogItemRequestPipeWriter.Request.Item.Representation.SamplePeriod != samplePeriod)
                    throw new ValidationException("All representations must be based on the same sample period.");
            }

            DataSourceController.ValidateParameters(begin, end, samplePeriod);

            /* pre-calculation */
            var bytesPerRow = catalogItemRequestPipeWriters
                .Sum(catalogItemRequestPipeWriter => catalogItemRequestPipeWriter.Request.Item.Representation.ElementSize);

            logger.LogTrace("A single row has a size of {BytesPerRow} bytes", bytesPerRow);

            var chunkSize = Math.Max(bytesPerRow, generalOptions.ReadChunkSize);
            logger.LogTrace("The chunk size is {ChunkSize} bytes", chunkSize);

            var rows = chunkSize / bytesPerRow;
            logger.LogTrace("{RowCount} rows will be processed per chunk", rows);

#warning Check if # of rows is 0 and throw an exception in this case

            var maxPeriodPerRequest = TimeSpan.FromTicks(samplePeriod.Ticks * rows);
            logger.LogTrace("The maximum period per request is {MaxPeriodPerRequest}", maxPeriodPerRequest);

            /* periods */
            var totalPeriod = end - begin;
            logger.LogTrace("The total period is {TotalPeriod}", totalPeriod);

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

                var currentBegin = begin + consumedPeriod;
                var currentEnd = currentBegin + currentPeriod;

                logger.LogTrace("Process period {CurrentBegin} to {CurrentEnd}", currentBegin, currentEnd);

                var readingTasks = readingGroups.Select(async readingGroup =>
                {
                    var (controller, catalogItemRequestPipeWriters) = readingGroup;

                    try
                    {
                        /* no need to remove handler because of short lifetime of IDataSource */
                        var dataSourceProgress = new Progress<double>();

                        dataSourceProgress.ProgressChanged += (sender, progressValue) =>
                        {
                            if (progressValue <= 1)
                            {
                                currentDataSourceProgress.AddOrUpdate(controller, progressValue, (_, _) => progressValue);

                                // https://stackoverflow.com/a/62768272 (currentDataSourceProgress)
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
                            catalogItemRequestPipeWriters,
                            dataSourceProgress,
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Process period {Begin} to {End} failed", currentBegin, currentEnd);
                    }
                });

#warning fail fast?
                await Task.WhenAll(readingTasks);

                /* continue in time */
                consumedPeriod += currentPeriod;
                remainingPeriod -= currentPeriod;

                progress?.Report(consumedPeriod.Ticks / (double)totalPeriod.Ticks);
            }

            /* complete */
            foreach (var readingGroup in readingGroups)
            {
                foreach (var catalogItemRequestPipeWriter in readingGroup.CatalogItemRequestPipeWriters)
                {
                    await catalogItemRequestPipeWriter.DataWriter.CompleteAsync();
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
                    var disposable = DataSource as IDisposable;
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
