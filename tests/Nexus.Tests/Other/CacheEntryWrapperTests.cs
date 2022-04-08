using Nexus.Core;
using Xunit;

namespace Other
{
    public class CacheEntryWrapperTests
    {
        [Fact]
        public async Task CanRead()
        {
            /*
             * _____
             * |   |
             * |___|__ end      _________________
             * |   |            uncached period 3
             * |   |
             * |   |            _________________
             * |xxx|              cached period 2
             * |xxx|
             * |xxx|            _________________
             * |   |            uncached period 2
             * |   |            _________________
             * |xxx|              cached period 1
             * |xxx|            _________________
             * |   |            uncached period 1
             * |___|__ begin    _________________
             * |   |
             * |___|__ file begin
             * 
             */

            // Arrange

            var fileBegin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var filePeriod = TimeSpan.FromDays(1);
            var samplePeriod = TimeSpan.FromHours(3);

            var stream = new MemoryStream();
            stream.Seek(8 * sizeof(double), SeekOrigin.Begin);
            stream.WriteByte(2);
            stream.Write(BitConverter.GetBytes(new DateTime(2020, 01, 01, 6, 0, 0, DateTimeKind.Utc).Ticks));
            stream.Write(BitConverter.GetBytes(new DateTime(2020, 01, 01, 15, 0, 0, DateTimeKind.Utc).Ticks));
            stream.Write(BitConverter.GetBytes(new DateTime(2020, 01, 01, 18, 0, 0, DateTimeKind.Utc).Ticks));
            stream.Write(BitConverter.GetBytes(new DateTime(2020, 01, 01, 21, 0, 0, DateTimeKind.Utc).Ticks));

            var wrapper = new CacheEntryWrapper(fileBegin, filePeriod, samplePeriod, stream);

            var begin = new DateTime(2020, 01, 01, 3, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 01, 21, 0, 0, DateTimeKind.Utc);
            var targetBuffer = new double[6];

            // Act
            var uncachedIntervals = await wrapper.ReadAsync(begin, end, targetBuffer, CancellationToken.None);

            // Assert
            var expected1 = new Interval(
                Begin: new DateTime(2020, 01, 01, 3, 0, 0, DateTimeKind.Utc),
                End: new DateTime(2020, 01, 01, 6, 0, 0, DateTimeKind.Utc));

            var expected2 = new Interval(
                Begin: new DateTime(2020, 01, 01, 15, 0, 0, DateTimeKind.Utc),
                End: new DateTime(2020, 01, 01, 18, 0, 0, DateTimeKind.Utc));

            Assert.Collection(uncachedIntervals,
                actual1 => Assert.Equal(expected1, actual1),
                actual2 => Assert.Equal(expected2, actual2));
        }
    }
}