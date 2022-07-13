using Nexus.Core;
using Nexus.DataModel;
using Nexus.Services;
using Nexus.Utilities;
using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Nexus.Extensibility
{
    internal interface IDataSourceController : IDisposable
    {
        Task InitializeAsync(
            ConcurrentDictionary<string, ResourceCatalog> catalogs,
            ILogger logger,
            CancellationToken cancellationToken);

        Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(
            string path,
            CancellationToken cancellationToken);

        Task<ResourceCatalog> GetCatalogAsync(
            string catalogId,
            CancellationToken cancellationToken);

        Task<CatalogAvailability> GetAvailabilityAsync(
            string catalogId, 
            DateTime begin,
            DateTime end,
            TimeSpan step,
            CancellationToken cancellationToken);

        Task<CatalogTimeRange> GetTimeRangeAsync(
            string catalogId,
            CancellationToken cancellationToken);

        Task<bool> IsDataOfDayAvailableAsync(
            string catalogId,
            DateTime day, 
            CancellationToken cancellationToken);

        Task ReadAsync(
            DateTime begin,
            DateTime end,
            TimeSpan samplePeriod, 
            CatalogItemRequestPipeWriter[] catalogItemRequestPipeWriters,
            ReadDataHandler readDataHandler,
            IProgress<double> progress, 
            CancellationToken cancellationToken);
    }

    internal class DataSourceController : IDataSourceController
    {
        #region Fields

        private IProcessingService _processingService;
        private ICacheService _cacheService;
        private DataOptions _dataOptions;
        private ConcurrentDictionary<string, ResourceCatalog> _catalogCache = default!;

        #endregion

        #region Constructors

        public DataSourceController(
            IDataSource dataSource, 
            DataSourceRegistration registration,
            JsonElement? systemConfiguration,
            JsonElement? requestConfiguration,
            IProcessingService processingService,
            ICacheService cacheService,
            DataOptions dataOptions,
            ILogger<DataSourceController> logger)
        {
            DataSource = dataSource;
            DataSourceRegistration = registration;
            SystemConfiguration = systemConfiguration;
            RequestConfiguration = requestConfiguration;
            Logger = logger;

            _processingService = processingService;
            _cacheService = cacheService;
            _dataOptions = dataOptions;
        }

        #endregion

        #region Properties

        private IDataSource DataSource { get; }

        private DataSourceRegistration DataSourceRegistration { get; }

        private JsonElement? SystemConfiguration { get; }

        internal JsonElement? RequestConfiguration { get; }

        private ILogger Logger { get; }

        #endregion

        #region Methods

        public async Task InitializeAsync(
            ConcurrentDictionary<string, ResourceCatalog> catalogCache,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            _catalogCache = catalogCache;

            var context = new DataSourceContext(
                ResourceLocator: DataSourceRegistration.ResourceLocator,
                SystemConfiguration: SystemConfiguration,
                SourceConfiguration: DataSourceRegistration.Configuration?.Clone(),
                RequestConfiguration: RequestConfiguration);

            await DataSource.SetContextAsync(context, logger, cancellationToken);
        }

        public async Task<CatalogRegistration[]> GetCatalogRegistrationsAsync(
            string path,
            CancellationToken cancellationToken)
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

        public async Task<ResourceCatalog> GetCatalogAsync(
            string catalogId,
            CancellationToken cancellationToken)
        {
            Logger.LogDebug("Load catalog {CatalogId}", catalogId);

            var catalog = await DataSource.GetCatalogAsync(catalogId, cancellationToken);
            
            catalog = catalog with 
            { 
                Resources = catalog.Resources?.OrderBy(resource => resource.Id).ToList() 
            };

            /* GetOrAdd is not working because it requires a synchronous delegate */
            _catalogCache.TryAdd(catalogId, catalog);

            return catalog;
        }

        public async Task<CatalogAvailability> GetAvailabilityAsync(
            string catalogId, 
            DateTime begin, 
            DateTime end,
            TimeSpan step,
            CancellationToken cancellationToken)
        {

            var count = (int)Math.Ceiling((end - begin).Ticks / (double)step.Ticks);
            var availabilities = new double[count];

            var tasks = new List<Task>(capacity: count);
            var currentBegin = begin;

            for (int i = 0; i < count; i++)
            {
                var currentEnd = currentBegin + step;
                var currentBegin_captured = currentBegin;
                var i_captured = i;

                tasks.Add(Task.Run(async () =>
                {
                    var availability = await DataSource.GetAvailabilityAsync(catalogId, currentBegin_captured, currentEnd, cancellationToken);
                    availabilities[i_captured] = availability;
                }));
                
                currentBegin = currentEnd;
            }

            await Task.WhenAll(tasks);

            return new CatalogAvailability(Data: availabilities);
        }

        public async Task<CatalogTimeRange> GetTimeRangeAsync(
            string catalogId,
            CancellationToken cancellationToken)
        {
            (var begin, var end) = await DataSource.GetTimeRangeAsync(catalogId, cancellationToken);

            return new CatalogTimeRange(
                Begin: begin,
                End: end);
        }

        public async Task<bool> IsDataOfDayAvailableAsync(
            string catalogId,
            DateTime day,
            CancellationToken cancellationToken)
        {
            return (await DataSource.GetAvailabilityAsync(catalogId, day, day.AddDays(1), cancellationToken)) > 0;
        }

        public async Task ReadAsync(
            DateTime begin,
            DateTime end,
            TimeSpan samplePeriod,
            CatalogItemRequestPipeWriter[] catalogItemRequestPipeWriters,
            ReadDataHandler readDataHandler,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            /* This method reads data from the data source or from the cache and optionally
             * processes the data (aggregation, resampling).
             * 
             * Normally, all data would be loaded at once using a single call to 
             * DataSource.ReadAsync(). But with caching involved, it is not uncommon
             * to have only parts of the requested data available in cache. The rest needs to
             * be loaded and processed as usual. This leads to fragmented read periods and thus
             * often more than a single call to DataSource.ReadAsync() is necessary.
             * 
             * However, during the first request the cache is filled and subsequent identical
             * requests will from now on be served from the cache only.
             */

            var memoryOwners = new List<IMemoryOwner<byte>>();

            try
            {
                /* preparation */
                var readUnits = PrepareReadUnits(
                    begin,
                    end, 
                    catalogItemRequestPipeWriters, 
                    memoryOwners);

                var readingTasks = new List<Task>(capacity: readUnits.Length);

#warning access to totalProgress (see below) is not thread safe
                var totalProgress = 0.0;

                /* original data */
                var originalReadUnits = readUnits
                    .Where(readUnit => readUnit.CatalogItemRequest.BaseItem is null)
                    .ToArray();

                Logger.LogTrace("Load {RepresentationCount} original representations", originalReadUnits.Length);

                var originalProgress = new Progress<double>();
                var originalProgressFactor = originalReadUnits.Length / (double)readUnits.Length;
                var originalProgress_old = 0.0;

                originalProgress.ProgressChanged += (sender, progressValue) =>
                {
                    var actualProgress = progressValue - originalProgress_old;
                    originalProgress_old = progressValue;
                    totalProgress += actualProgress;
                    progress.Report(totalProgress);
                };

                var originalTask = ReadOriginalAsync(
                    begin, 
                    end, 
                    samplePeriod,
                    originalReadUnits,
                    readDataHandler,
                    originalProgress,
                    cancellationToken);

                readingTasks.Add(originalTask);

                /* processing data */
                var processingReadUnits = readUnits
                    .Where(readUnit => readUnit.CatalogItemRequest.BaseItem is not null)
                    .ToArray();

                Logger.LogTrace("Load {RepresentationCount} processing representations", processingReadUnits.Length);

                var processingProgressFactor = 1 / (double)readUnits.Length;

                foreach (var processingReadUnit in processingReadUnits)
                {
                    var processingProgress = new Progress<double>();
                    var processingProgress_old = 0.0;

                    processingProgress.ProgressChanged += (sender, progressValue) =>
                    {
                        var actualProgress = progressValue - processingProgress_old;
                        processingProgress_old = progressValue;
                        totalProgress += actualProgress;
                        progress.Report(totalProgress);
                    };

                    var processingTask = ReadProcessingAsync(
                        begin, 
                        end, 
                        processingReadUnit,
                        readDataHandler,
                        processingProgress,
                        cancellationToken);

                    readingTasks.Add(processingTask);
                }

                /* wait for tasks to finish */
#warning fail fast?
                await Task.WhenAll(readingTasks);
            }
            finally
            {
                foreach (var memoryOwner in memoryOwners)
                {
                    memoryOwner.Dispose();
                }
            }
        }

        private async Task ReadOriginalAsync(
            DateTime begin,
            DateTime end, 
            TimeSpan samplePeriod,
            ReadUnit[] originalUnits,
            ReadDataHandler readDataHandler,
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            var readRequests = originalUnits
                .Select(readUnit => readUnit.ReadRequest)
                .ToArray();

            try
            {
                await DataSource.ReadAsync(
                    begin,
                    end,
                    readRequests,
                    readDataHandler,
                    progress,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Read original data period {Begin} to {End} failed", begin, end);
            }

            var targetElementCount = ExtensibilityUtilities.CalculateElementCount(begin, end, samplePeriod);
            var targetByteCount = sizeof(double) * targetElementCount;
            var readingTasks = new List<Task>(capacity: originalUnits.Length);

            foreach (var readUnit in originalUnits)
            {
                var (readRequest, catalogItemRequest, dataWriter) = readUnit;
                var (_, data, status) = readRequest;

                using var scope = Logger.BeginScope(new Dictionary<string, object>()
                {
                    ["ResourcePath"] = catalogItemRequest.Item.ToPath()
                });

                cancellationToken.ThrowIfCancellationRequested();

                var buffer = dataWriter
                    .GetMemory(targetByteCount)
                    .Slice(0, targetByteCount);

                var targetBuffer = new CastMemoryManager<byte, double>(buffer).Memory;

                readingTasks.Add(Task.Run(async () =>
                {
                    BufferUtilities.ApplyRepresentationStatusByDataType(
                        catalogItemRequest.Item.Representation.DataType,
                        data,
                        status,
                        target: targetBuffer);

                    /* update progress */
                    Logger.LogTrace("Advance data pipe writer by {DataLength} bytes", targetByteCount);
                    dataWriter.Advance(targetByteCount);
                    await dataWriter.FlushAsync();
                }));
            }

            /* wait for tasks to finish */
#warning fail fast?
            await Task.WhenAll(readingTasks);
        }

        private async Task ReadProcessingAsync(
           DateTime begin,
           DateTime end,
           ReadUnit readUnit,
           ReadDataHandler readDataHandler,
           IProgress<double> progress,
           CancellationToken cancellationToken)
        {
            var item = readUnit.CatalogItemRequest.Item;
            var baseItem = readUnit.CatalogItemRequest.BaseItem!;
            var ignoreCache = _dataOptions.DisableCache || item.Representation.Kind == RepresentationKind.Resampled;

            /* target buffer */
            var targetElementCount = ExtensibilityUtilities.CalculateElementCount(begin, end, item.Representation.SamplePeriod);
            var targetByteCount = sizeof(double) * targetElementCount;

            var buffer = readUnit.DataWriter
               .GetMemory(targetByteCount)
               .Slice(0, targetByteCount);

            var targetBuffer = new CastMemoryManager<byte, double>(buffer).Memory;

            try
            {
                /* load data from cache */
                Logger.LogTrace("Load data from cache");

                List<Interval> uncachedIntervals;

                if (ignoreCache)
                {
                    uncachedIntervals = new List<Interval> { new Interval(begin, end) };
                }

                else
                {
                    uncachedIntervals = await _cacheService.ReadAsync(
                        readUnit.CatalogItemRequest.Item,
                        begin,
                        targetBuffer,
                        cancellationToken);
                }

                /* load and process remaining data from source */
                Logger.LogTrace("Load and process {PeriodCount} uncached periods from source", uncachedIntervals.Count);

                var readRequest = readUnit.ReadRequest;
                var elementSize = baseItem.Representation.ElementSize;
                var sourceSamplePeriod = baseItem.Representation.SamplePeriod;
                var targetSamplePeriod = item.Representation.SamplePeriod;

                var blockSize = item.Representation.Kind == RepresentationKind.Resampled
                    ? (int)(sourceSamplePeriod.Ticks / targetSamplePeriod.Ticks)
                    : (int)(targetSamplePeriod.Ticks / sourceSamplePeriod.Ticks);

                foreach (var interval in uncachedIntervals)
                {
                    var samplePeriod = item.Representation.SamplePeriod;
                    var baseSamplePeriod = baseItem.Representation.SamplePeriod;

                    var offset = interval.Begin - begin;
                    var length = interval.End - interval.Begin;

                    var slicedTargetBuffer = targetBuffer.Slice(
                        start: NexusUtilities.Scale(offset, samplePeriod),
                        length: NexusUtilities.Scale(length, samplePeriod));

                    var slicedReadRequest = readRequest with
                    {
                        Data = readRequest.Data.Slice(
                            start: NexusUtilities.Scale(offset, baseSamplePeriod) * elementSize,
                            length: NexusUtilities.Scale(length, baseSamplePeriod) * elementSize),

                        Status = readRequest.Status.Slice(
                            start: NexusUtilities.Scale(offset, baseSamplePeriod),
                            length: NexusUtilities.Scale(length, baseSamplePeriod)),
                    };

                    /* read */
                    await DataSource.ReadAsync(
                        interval.Begin,
                        interval.End,
                        new[] { slicedReadRequest },
                        readDataHandler,
                        progress,
                        cancellationToken);

                    /* process */
                    _processingService.Process(
                        baseItem.Representation.DataType,
                        item.Representation.Kind,
                        slicedReadRequest.Data,
                        slicedReadRequest.Status,
                        targetBuffer: slicedTargetBuffer,
                        blockSize);
                }

                /* update cache */
                if (!ignoreCache)
                {
                    await _cacheService.UpdateAsync(
                        readUnit.CatalogItemRequest.Item,
                        begin,
                        targetBuffer,
                        uncachedIntervals,
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Read processing data period {Begin} to {End} failed", begin, end);
            }

            /* update progress */
            Logger.LogTrace("Advance data pipe writer by {DataLength} bytes", targetByteCount);
            readUnit.DataWriter.Advance(targetByteCount);
            await readUnit.DataWriter.FlushAsync();
        }

        private ReadUnit[] PrepareReadUnits(
            DateTime begin,
            DateTime end,
            CatalogItemRequestPipeWriter[] catalogItemRequestPipeWriters, 
            List<IMemoryOwner<byte>> memoryOwners)
        {
            var readUnits = new List<ReadUnit>();

            foreach (var catalogItemRequestPipeWriter in catalogItemRequestPipeWriters)
            {
                var (catalogItemRequest, dataWriter) = catalogItemRequestPipeWriter;

                var item = catalogItemRequest.BaseItem is null
                    ? catalogItemRequest.Item 
                    : catalogItemRequest.BaseItem;

                /* buffers */
                var elementCount = ExtensibilityUtilities.CalculateElementCount(begin, end, item.Representation.SamplePeriod);
                var (dataOwner, dataMemory, statusOwner, statusMemory) = PrepareBuffers(elementCount, item.Representation.ElementSize);

                memoryOwners.Add(dataOwner);
                memoryOwners.Add(statusOwner);

                /* catalog */

                /* _catalogMap is guaranteed to contain the current catalog 
                 * because GetCatalogAsync is called before ReadAsync */
                if (_catalogCache.TryGetValue(item.Catalog.Id, out var catalog))
                {
                    var originalCatalogItem = catalog.Find(item.ToPath());
                    var readRequest = new ReadRequest(originalCatalogItem, dataMemory, statusMemory);
                    var readUnit = new ReadUnit(readRequest, catalogItemRequest, dataWriter);

                    readUnits.Add(readUnit);
                }

                else
                {
                    throw new Exception($"Cannot find catalog {item.Catalog.Id}.");
                }
            }

            return readUnits.ToArray();
        }

        #endregion

        #region Static Methods

        public static Task ReadAsync(
            DateTime begin,
            DateTime end,
            TimeSpan samplePeriod,
            DataReadingGroup[] readingGroups,
            ReadDataHandler readDataHandler,
            DataOptions dataOptions,
            IProgress<double>? progress,
            ILogger<DataSourceController> logger,
            CancellationToken cancellationToken)
        {
            /* validation */
            DataSourceController.ValidateParameters(begin, end, samplePeriod);

            var catalogItemRequestPipeWriters = readingGroups.SelectMany(readingGroup => readingGroup.CatalogItemRequestPipeWriters);

            if (!catalogItemRequestPipeWriters.Any())
                return Task.CompletedTask; 

            foreach (var catalogItemRequestPipeWriter in catalogItemRequestPipeWriters)
            {
                /* All frequencies are required to be multiples of each other, namely these are:
                 * 
                 * - begin
                 * - end
                 * - item -> representation -> sample period
                 * - base item -> representation -> sample period
                 * 
                 * This makes aggregation and caching much easier. A drawback of this approach
                 * is that a user who selects e.g. a 10-minute value that should be resampled 
                 * to 1 s, is required to also choose the begin and end parameters to be a 
                 * multiple of 10-minutes. Selecting a time period < 10 minutes is not possible.
                 */

                var request = catalogItemRequestPipeWriter.Request;
                var itemSamplePeriod = request.Item.Representation.SamplePeriod;

                if (itemSamplePeriod != samplePeriod)
                    throw new ValidationException("All representations must be based on the same sample period.");

                if (request.BaseItem is not null)
                {
                    var baseItemSamplePeriod = request.BaseItem.Representation.SamplePeriod;
                    DataSourceController.ValidateParameters(begin, end, baseItemSamplePeriod);

                    // resampling is only possible if base sample period < sample period
                    if (request.Item.Representation.Kind == RepresentationKind.Resampled)
                    {
                        if (baseItemSamplePeriod < samplePeriod)
                            throw new ValidationException("Unable to resample data if the base sample period is <= the sample period.");

                        if (baseItemSamplePeriod.Ticks % itemSamplePeriod.Ticks != 0)
                            throw new ValidationException("For resampling, the base sample period must be a multiple of the sample period.");
                    }

                    // aggregation is only possible if sample period > base sample period
                    else
                    {
                        if (samplePeriod < baseItemSamplePeriod)
                            throw new ValidationException("Unable to aggregate data if the sample period is <= the base sample period.");

                        if (itemSamplePeriod.Ticks % baseItemSamplePeriod.Ticks != 0)
                            throw new ValidationException("For aggregation, the sample period must be a multiple of the base sample period.");
                    }
                }
            }

            /* pre-calculation */
            var bytesPerRow = 0L;
            var largestSamplePeriod = samplePeriod;

            foreach (var catalogItemRequestPipeWriter in catalogItemRequestPipeWriters)
            {
                var request = catalogItemRequestPipeWriter.Request;

                var elementSize = request.Item.Representation.ElementSize;
                var elementCount = 1L;

                if (request.BaseItem is not null)
                {
                    var baseItemSamplePeriod = request.BaseItem.Representation.SamplePeriod;
                    var itemSamplePeriod = request.Item.Representation.SamplePeriod;

                    if (request.Item.Representation.Kind == RepresentationKind.Resampled)
                    {
                        if (largestSamplePeriod < baseItemSamplePeriod)
                            largestSamplePeriod = baseItemSamplePeriod;
                    }

                    else
                    {
                        elementCount =
                            baseItemSamplePeriod.Ticks /
                            itemSamplePeriod.Ticks;
                    }
                }

                bytesPerRow += Math.Max(1, elementCount) * elementSize;
            }

            logger.LogTrace("A single row has a size of {BytesPerRow} bytes", bytesPerRow);

            var chunkSize = Math.Max(bytesPerRow, dataOptions.ReadChunkSize);
            logger.LogTrace("The chunk size is {ChunkSize} bytes", chunkSize);

            var rowCount = chunkSize / bytesPerRow;
            logger.LogTrace("{RowCount} rows can be processed per chunk", rowCount);

            var maxPeriodPerRequest = TimeSpan
                .FromTicks(samplePeriod.Ticks * rowCount)
                .RoundDown(largestSamplePeriod);

            if (maxPeriodPerRequest == TimeSpan.Zero)
                throw new ValidationException("Unable to load the requested data because the available chunk size is too low.");

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
            return Task.Run(async () =>
            {
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
                                    // https://stackoverflow.com/a/62768272 (currentDataSourceProgress)
                                    currentDataSourceProgress.AddOrUpdate(controller, progressValue, (_, _) => progressValue);

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
                                readDataHandler,
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
            });
        }

        private static (IMemoryOwner<byte>, Memory<byte>, IMemoryOwner<byte>, Memory<byte>) PrepareBuffers(int elementCount, int elementSize)
        {
            var byteCount = elementCount * elementSize;

            /* data memory */
            var dataOwner = MemoryPool<byte>.Shared.Rent(byteCount);
            var dataMemory = dataOwner.Memory.Slice(0, byteCount);
            dataMemory.Span.Clear();

            /* status memory */
            var statusOwner = MemoryPool<byte>.Shared.Rent(elementCount);
            var statusMemory = statusOwner.Memory.Slice(0, elementCount);
            statusMemory.Span.Clear();

            return (dataOwner, dataMemory, statusOwner, statusMemory);
        }

        private static void ValidateParameters(
            DateTime begin, 
            DateTime end, 
            TimeSpan samplePeriod)
        {
            /* When the user requests two time series of the same frequency, they will be aligned to the sample
             * period. With the current implementation, it simply not possible for one data source to provide an 
             * offset which is smaller than the sample period. In future a solution could be to have time series 
             * data with associated time stamps, which is not yet implemented.
             */

            /* Examples
             * 
             *   OK: from 2020-01-01 00:00:01.000 to 2020-01-01 00:00:03.000 @ 1 s
             * 
             * FAIL: from 2020-01-01 00:00:00.000 to 2020-01-02 00:00:00.000 @ 130 ms
             *   OK: from 2020-01-01 00:00:00.050 to 2020-01-02 00:00:00.000 @ 130 ms
             *   
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
