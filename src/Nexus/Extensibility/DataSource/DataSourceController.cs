using Nexus.Core;
using Nexus.DataModel;
using Nexus.Services;
using Nexus.Utilities;
using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;

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
            IProgress<double> progress, 
            CancellationToken cancellationToken);
    }

    internal class DataSourceController : IDataSourceController
    {
        #region Fields

        private IProcessingService _processingService;
        private ICacheService _cacheService;
        private ConcurrentDictionary<string, ResourceCatalog> _catalogCache = null!;

        #endregion

        #region Constructors

        public DataSourceController(
            IDataSource dataSource, 
            DataSourceRegistration registration,
            IReadOnlyDictionary<string, string> userConfiguration,
            IProcessingService processingService,
            ICacheService cacheService,
            ILogger<DataSourceController> logger)
        {
            DataSource = dataSource;
            DataSourceRegistration = registration;
            UserConfiguration = userConfiguration;
            Logger = logger;

            _processingService = processingService;
            _cacheService = cacheService;
        }

        #endregion

        #region Properties

        private IDataSource DataSource { get; }

        private DataSourceRegistration DataSourceRegistration { get; }

        internal IReadOnlyDictionary<string, string> UserConfiguration { get; }

        private ILogger Logger { get; }

        #endregion

        #region Methods

        public async Task InitializeAsync(
            ConcurrentDictionary<string, ResourceCatalog> catalogCache,
            ILogger logger,
            CancellationToken cancellationToken)
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

            /* GetOrAdd is not working because it requires a synchronous delegate */
            _catalogCache.TryAdd(catalogId, catalog);

            return catalog;
        }

        public async Task<CatalogAvailability> GetAvailabilityAsync(
            string catalogId, 
            DateTime begin, 
            DateTime end, 
            CancellationToken cancellationToken)
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
            IProgress<double> progress,
            CancellationToken cancellationToken)
        {
            var memoryOwners = new List<IMemoryOwner<byte>>();

            try
            {
                /* 1. prepare read units */
                var (originalUnits, processingUnits) = await PrepareReadUnitsAsync(
                    begin,
                    end, 
                    catalogItemRequestPipeWriters, 
                    memoryOwners);

                /* 2. read data from sources */
                
                /* 2.1 original data */
                var readRequests = originalUnits
                    .Select(readUnit => readUnit.ReadRequest)
                    .ToArray();

                await DataSource.ReadAsync(
                    begin,
                    end,
                    readRequests,
                    progress,
                    cancellationToken);

                /* 2.2 processing data */
                foreach (var readUnit in processingUnits)
                {
                    var slices = readUnit.Slices!
                        .Where(slice => !slice.FromCache)
                        .ToArray();

                    foreach (var slice in slices)
                    {
                        var readRequest = readUnit.ReadRequest;
                        var elementSize = readRequest.CatalogItem.Representation.ElementSize;

                        var slicedReadRequest = readRequest with
                        {
                            Data = readRequest.Data.Slice(slice.Offset * elementSize, slice.Length * elementSize),
                            Status = readRequest.Status.Slice(slice.Offset, slice.Length),
                        };

                        await DataSource.ReadAsync(
                            slice.Begin,
                            slice.End,
                            new[] { slicedReadRequest },
                            progress,
                            cancellationToken);
                    }
                }

                /* 3. write data to pipes */
                var targetElementCount = ExtensibilityUtilities.CalculateElementCount(begin, end, samplePeriod);
                var targetByteCount = sizeof(double) * targetElementCount;
                var readingTasks = new List<Task>(capacity: catalogItemRequestPipeWriters.Length);

                foreach (var readUnit in originalUnits.Concat(processingUnits))
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
                        /* 3.1 original data */
                        if (catalogItemRequest.BaseItem is null)
                        {
                            BufferUtilities.ApplyRepresentationStatusByDataType(
                                catalogItemRequest.Item.Representation.DataType,
                                data,
                                status,
                                target: targetBuffer);
                        }

                        /* 3.2 processing data */
                        else
                        {
                            Logger.LogTrace("Process data");

                            var baseItem = catalogItemRequest.BaseItem;
                            var elementSize = baseItem.Representation.ElementSize;
                            var item = catalogItemRequest.Item;
                            var sourceSamplePeriod = baseItem.Representation.SamplePeriod;
                            var targetSamplePeriod = item.Representation.SamplePeriod;
                            var blockSize = (int)(targetSamplePeriod.Ticks / sourceSamplePeriod.Ticks);

                            foreach (var slice in readUnit.Slices!)
                            {
                                var offset = (int)((slice.Begin - begin).Ticks / samplePeriod.Ticks);
                                var length = (int)((slice.End - slice.Begin).Ticks / samplePeriod.Ticks);
                                var slicedTargetBuffer = targetBuffer.Slice(offset, length);

                                /* 3.2.1 load from cache */
                                if (slice.FromCache)
                                {
                                    await _cacheService.LoadAsync(slicedTargetBuffer);
                                }

                                /* 3.2.2 calculate from loaded data */
                                else
                                {
                                    _processingService.Process(
                                        item.Representation.DataType,
                                        item.Representation.Kind,
                                        readRequest.Data.Slice(slice.Offset * elementSize, slice.Length * elementSize),
                                        readRequest.Status.Slice(slice.Offset, slice.Length),
                                        targetBuffer: slicedTargetBuffer,
                                        blockSize);

#warning _cacheService.SaveAsync
                                }
                            }
                        }

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
            finally
            {
                foreach (var memoryOwner in memoryOwners)
                {
                    memoryOwner.Dispose();
                }
            }
        }

        private async Task<(ReadUnit[] OriginalUnits, ReadUnit[] ProcessingUnits)> PrepareReadUnitsAsync(
            DateTime begin,
            DateTime end,
            CatalogItemRequestPipeWriter[] catalogItemRequestPipeWriters, 
            List<IMemoryOwner<byte>> memoryOwners)
        {
            var originalUnits = new List<ReadUnit>();
            var processingUnits = new List<ReadUnit>();

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

                    /* original data */
                    if (readUnit.CatalogItemRequest.BaseItem is null ||
                        readUnit.CatalogItemRequest.Item.Representation.Kind == RepresentationKind.Resampled)
                    {
                        originalUnits.Add(readUnit);
                    }

                    /* post processing */
                    else
                    {
                        var currentSamplePeriod = readUnit.ReadRequest.CatalogItem.Representation.SamplePeriod;
                        var slices = await NexusCoreUtilities.CalculateSlicesAsync(begin, end, currentSamplePeriod, _cacheService);

                        readUnit.Slices = slices;
                        processingUnits.Add(readUnit);
                    }
                }

                else
                {
                    throw new Exception($"Cannot find catalog {item.Catalog.Id}.");
                }
            }

            return (originalUnits.ToArray(), processingUnits.ToArray());
        }

        #endregion

        #region Static Methods

        public static async Task ReadAsync(
            DateTime begin,
            DateTime end,
            TimeSpan samplePeriod,
            DataReadingGroup[] readingGroups,
            DataOptions dataOptions,
            IProgress<double>? progress,
            ILogger<DataSourceController> logger,
            CancellationToken cancellationToken)
        {
            /* validation */
            DataSourceController.ValidateParameters(begin, end, samplePeriod);

            var catalogItemRequestPipeWriters = readingGroups.SelectMany(readingGroup => readingGroup.CatalogItemRequestPipeWriters);

            if (!catalogItemRequestPipeWriters.Any())
                return;

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
                 * is that for a user who selects e.g. a 10-minute value that should be resampled 
                 * to 1 s, it is required to also choose the begin and end parameters to be a 
                 * multiple of 10-minutes. Selecting a time period < 10 minutes is not possible.
                 * 
                 */

                var request = catalogItemRequestPipeWriter.Request;

                if (request.Item.Representation.SamplePeriod != samplePeriod)
                    throw new ValidationException("All representations must be based on the same sample period.");

                if (request.BaseItem is not null)
                {
                    var baseItemSamplePeriod = request.BaseItem.Representation.SamplePeriod;
                    DataSourceController.ValidateParameters(begin, end, baseItemSamplePeriod);

                    // resampling is only possible if base sample period <= sample period
                    if (request.Item.Representation.Kind == RepresentationKind.Resampled)
                    {
                        if (baseItemSamplePeriod < samplePeriod)
                            throw new ValidationException("Unable to resample data if the base sample period is <= the sample period.");
                    }

                    // aggregation is only possible if sample period > base sample period
                    else
                    {
                        if (samplePeriod < baseItemSamplePeriod)
                            throw new ValidationException("Unable to aggregate data if the sample period is <= the base sample period.");
                    }
                }
            }

            /* pre-calculation */
            var bytesPerRow = catalogItemRequestPipeWriters.Sum(catalogItemRequestPipeWriter =>
            {
                var request = catalogItemRequestPipeWriter.Request;
                var elementSize = catalogItemRequestPipeWriter.Request.Item.Representation.ElementSize;
                var elementCount = 1L;

                if (request.BaseItem is not null)
                {
                    elementCount = 
                        request.BaseItem.Representation.SamplePeriod.Ticks / 
                        request.Item.Representation.SamplePeriod.Ticks;
                }

                return Math.Max(1, elementCount) * elementSize;
            });

            logger.LogTrace("A single row has a size of {BytesPerRow} bytes", bytesPerRow);

            var chunkSize = Math.Max(bytesPerRow, dataOptions.ReadChunkSize);
            logger.LogTrace("The chunk size is {ChunkSize} bytes", chunkSize);

            var rowCount = chunkSize / bytesPerRow;
            logger.LogTrace("{RowCount} rows will be processed per chunk", rowCount);

            if (rowCount == 0)
                throw new ValidationException("Unable to load the requested data because the available chunk size is too low.");

            var maxPeriodPerRequest = TimeSpan.FromTicks(samplePeriod.Ticks * rowCount);
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
