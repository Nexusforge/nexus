using Moq;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Services;
using System.Runtime.InteropServices;
using Xunit;

namespace Services
{
    public class CacheServiceTests
    {
        delegate bool GobbleReturns(CatalogItem catalogItem, DateTime begin, out Stream cacheEntry);

        [Fact]
        public async Task CanReadCache()
        {
            // Arrange
            var expected = new double[]
            {
                0, 0, 2.2, 3.3, 4.4, 0, 6.6, 0,
                0, 0, 0, 0, 0, 0, 0, 0,
                0, 10, 20, 30, 40, 50, 60, 70
            };

            var databaseService = Mock.Of<IDatabaseService>();

            Mock.Get(databaseService)
                .Setup(databaseService => databaseService.TryReadCacheEntry(
                    It.IsAny<CatalogItem>(), 
                    It.IsAny<DateTime>(),
                    out It.Ref<Stream?>.IsAny))
                .Returns(new GobbleReturns((CatalogItem catalogItem, DateTime begin, out Stream cacheEntry) =>
                {
                    cacheEntry = new MemoryStream();
                    var writer = new BinaryWriter(cacheEntry);

                    if (begin == new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc))
                    {
                        var cachedIntervals = new[]
                        {
                            new Interval(new DateTime(2020, 01, 01, 6, 0, 0, DateTimeKind.Utc), new DateTime(2020, 01, 01, 15, 0, 0, DateTimeKind.Utc)),
                            new Interval(new DateTime(2020, 01, 01, 18, 0, 0, DateTimeKind.Utc), new DateTime(2020, 01, 01, 21, 0, 0, DateTimeKind.Utc))
                        };

                        for (int i = 0; i < 8; i++)
                        {
                            writer.Write(i * 1.1);
                        }

                        CacheEntryWrapper.WriteCachedIntervals(cacheEntry, cachedIntervals);

                        return true;
                    }

                    else if (begin == new DateTime(2020, 01, 02, 0, 0, 0, DateTimeKind.Utc))
                    {
                        return false;
                    }

                    else if (begin == new DateTime(2020, 01, 03, 0, 0, 0, DateTimeKind.Utc))
                    {
                        var cachedIntervals = new[]
                        {
                            new Interval(new DateTime(2020, 01, 03, 0, 0, 0, DateTimeKind.Utc), new DateTime(2020, 01, 04, 0, 0, 0, DateTimeKind.Utc))
                        };

                        for (int i = 0; i < 8; i++)
                        {
                            writer.Write(i * 10.0);
                        }

                        CacheEntryWrapper.WriteCachedIntervals(cacheEntry, cachedIntervals);

                        return true;
                    }

                    else
                    {
                        throw new Exception("This should never happen.");
                    }
                }));

            var cacheService = new CacheService(databaseService);
            var catalogItem = new CatalogItem(default!, default!, new Representation(NexusDataType.FLOAT64, TimeSpan.FromHours(3)));
            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var actual = new double[24];

            // Act
            var uncachedIntervals = await cacheService
                .ReadAsync(catalogItem, begin, actual, CancellationToken.None);

            // Assert
            Assert.Equal(expected.Length, actual.Length);

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], actual[i], precision: 1);
            }

            var expected1 = new Interval(
                Begin: new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc),
                End: new DateTime(2020, 01, 01, 6, 0, 0, DateTimeKind.Utc));

            var expected2 = new Interval(
                Begin: new DateTime(2020, 01, 01, 15, 0, 0, DateTimeKind.Utc),
                End: new DateTime(2020, 01, 01, 18, 0, 0, DateTimeKind.Utc));

            var expected3 = new Interval(
                Begin: new DateTime(2020, 01, 01, 21, 0, 0, DateTimeKind.Utc),
                End: new DateTime(2020, 01, 03, 0, 0, 0, DateTimeKind.Utc));

            Assert.Collection(uncachedIntervals,
                actual1 => Assert.Equal(expected1, actual1),
                actual2 => Assert.Equal(expected2, actual2),
                actual3 => Assert.Equal(expected3, actual3));
        }

        [Fact]
        public async Task CanUpdateCache()
        {
            // Arrange
            var expectedData1 = new double[] { 0, 0, 0, 0, 0, 5, 6, 7 };
            var expectedData2 = new double[] { 8, 0, 0, 0, 0, 0, 0, 0 };

            var expected = new DateTime[]
            {
                new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2020, 01, 02, 0, 0, 0, DateTimeKind.Utc)
            };

            var databaseService = Mock.Of<IDatabaseService>();
            var actualBegins = new List<DateTime>();
            var actualStreams = new List<MemoryStream>();

            Mock.Get(databaseService)
               .Setup(databaseService => databaseService.TryWriteCacheEntry(
                   It.IsAny<CatalogItem>(),
                   It.IsAny<DateTime>(),
                   out It.Ref<Stream?>.IsAny))
               .Returns(new GobbleReturns((CatalogItem catalogItem, DateTime begin, out Stream cacheEntry) =>
               {
                   var stream = new MemoryStream();

                   cacheEntry = stream;
                   actualStreams.Add(stream);
                   actualBegins.Add(begin);

                   return true;
               }));

            var cacheService = new CacheService(databaseService);
            var catalogItem = new CatalogItem(default!, default!, new Representation(NexusDataType.FLOAT64, TimeSpan.FromHours(3)));

            var sourceBuffer = Enumerable.Range(0, 24)
                .Select(value => (double)value).ToArray();

            var uncachedIntervals = new List<Interval>
            {
                new Interval(new DateTime(2020, 01, 01, 15, 0, 0, DateTimeKind.Utc), new DateTime(2020, 01, 02, 03, 0, 0, DateTimeKind.Utc))
            };

            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);

            // Act
            await cacheService
                .UpdateAsync(catalogItem, begin, sourceBuffer, uncachedIntervals, CancellationToken.None);

            // Assert
            Assert.True(expected.SequenceEqual(actualBegins));

            var actualData1 = MemoryMarshal.Cast<byte, double>(actualStreams[0].GetBuffer().AsSpan()).Slice(0, 8).ToArray();
            Assert.True(expectedData1.SequenceEqual(actualData1));

            var actualData2 = MemoryMarshal.Cast<byte, double>(actualStreams[1].GetBuffer().AsSpan()).Slice(0, 8).ToArray();
            Assert.True(expectedData2.SequenceEqual(actualData2));
        }
    }
}
