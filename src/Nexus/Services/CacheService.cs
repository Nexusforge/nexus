using Nexus.Core;
using Nexus.DataModel;
using Nexus.Utilities;

namespace Nexus.Services
{
    internal interface ICacheService
    {
        Task<List<Interval>> ReadAsync(
            CatalogItem catalogItem,
            DateTime begin,
            DateTime end,
            Memory<double> targetBuffer,
            CancellationToken cancellationToken);

        Task UpdateAsync(
            CatalogItem catalogItem,
            Memory<double> targetBuffer, 
            List<Interval> uncachedIntervals,
            CancellationToken cancellationToken);
    }

    internal class CacheService : ICacheService
    {
        private IDatabaseService _databaseService;
        private TimeSpan _largestSamplePeriod = TimeSpan.FromDays(1);

        public CacheService(
            IDatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        public async Task<List<Interval>> ReadAsync(
            CatalogItem catalogItem,
            DateTime begin,
            DateTime end,
            Memory<double> targetBuffer,
            CancellationToken cancellationToken)
        {
            var elementSize = catalogItem.Representation.ElementSize;
            var samplePeriod = catalogItem.Representation.SamplePeriod;
            var filePeriod = GetFilePeriod(samplePeriod);
            var uncachedIntervals = new List<Interval>();

            /* try read data from cache */
            await NexusUtilities.FileLoopAsync(begin, end, filePeriod, async (fileBegin, fileOffset, duration) =>
            {
                var actualBegin = fileBegin + fileOffset;
                var actualEnd = actualBegin + duration;

                if (_databaseService.TryReadCacheEntry(catalogItem, fileBegin, out var cacheEntry))
                {
                    var slicedTargetBuffer = targetBuffer.Slice(
                       start: NexusUtilities.Scale(fileOffset, samplePeriod),
                       length: NexusUtilities.Scale(duration, samplePeriod));

                    try
                    {
                        using var cacheEntryWrapper = new CacheEntryWrapper(
                            fileBegin, filePeriod, samplePeriod, elementSize, cacheEntry);

                        var moreUncachedIntervals = await cacheEntryWrapper.ReadAsync(
                            actualBegin, 
                            actualEnd, 
                            slicedTargetBuffer,
                            cancellationToken);

                        uncachedIntervals.AddRange(moreUncachedIntervals);
                    }
                    catch
                    {
                        uncachedIntervals.Add(new Interval(actualBegin, actualEnd));
                    }
                }

                else
                {
                    uncachedIntervals.Add(new Interval(actualBegin, actualEnd));
                }
            });

            /* consolidate periods */
            var consolidatedPeriods = new List<Interval>() { uncachedIntervals[0] };

            for (int i = 1; i < uncachedIntervals.Count; i++)
            {
                if (consolidatedPeriods[^1].End == uncachedIntervals[i].Begin)
                    consolidatedPeriods[^1] = consolidatedPeriods[^1] with { End = uncachedIntervals[i].End };

                else
                    consolidatedPeriods.Add(uncachedIntervals[i]);
            }

            return consolidatedPeriods;
        }

        public async Task UpdateAsync(
            CatalogItem catalogItem,
            Memory<double> targetBuffer,
            List<Interval> uncachedIntervals,
            CancellationToken cancellationToken)
        {
            var elementSize = catalogItem.Representation.ElementSize;
            var begin = uncachedIntervals.First().Begin;
            var end = uncachedIntervals.Last().End;
            var samplePeriod = catalogItem.Representation.SamplePeriod;
            var filePeriod = GetFilePeriod(samplePeriod);

            /* try write data to cache */
            foreach (var period in uncachedIntervals)
            {
                await NexusUtilities.FileLoopAsync(period.Begin, period.End, filePeriod, async (fileBegin, fileOffset, duration) =>
                {
                    var actualBegin = fileBegin + fileOffset;
                    var actualEnd = actualBegin + duration;

                    if (_databaseService.TryWriteCacheEntry(catalogItem, fileBegin, out var cacheEntry))
                    {
                        var slicedTargetBuffer = targetBuffer.Slice(
                           start: NexusUtilities.Scale(fileOffset, samplePeriod),
                           length: NexusUtilities.Scale(duration, samplePeriod));

                        try
                        {
                            using var cacheEntryWrapper = new CacheEntryWrapper(
                                fileBegin, filePeriod, samplePeriod, elementSize, cacheEntry);

                            await cacheEntryWrapper.WriteAsync(
                                actualBegin, 
                                actualEnd,
                                slicedTargetBuffer, 
                                cancellationToken);
                        }
                        catch
                        {
                            //
                        }
                    }
                });
            }
        }

        private TimeSpan GetFilePeriod(TimeSpan samplePeriod)
        {
            if (samplePeriod > _largestSamplePeriod || TimeSpan.FromDays(1).Ticks % samplePeriod.Ticks <= 0)
                throw new Exception("Caching is only supported for sample periods fit exactly into a single day.");

            return samplePeriod switch
            {
                _ when samplePeriod <= TimeSpan.FromSeconds(1e-9) => TimeSpan.FromSeconds(1e-3),
                _ when samplePeriod <= TimeSpan.FromSeconds(1e-6) => TimeSpan.FromSeconds(1e+0),
                _ when samplePeriod <= TimeSpan.FromSeconds(1e-3) => TimeSpan.FromHours(1),
                _ => TimeSpan.FromDays(1),
            };
        }
    }
}
