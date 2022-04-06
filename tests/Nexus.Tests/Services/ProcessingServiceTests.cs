using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Services;
using System.Runtime.InteropServices;
using Xunit;

namespace Services
{
    public class ProcessingServiceTests
    {
        [InlineData(RepresentationKind.Min,         0.90, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 },  new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, -4)]
        [InlineData(RepresentationKind.Min,         0.99, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 },  new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, double.NaN)]

        [InlineData(RepresentationKind.Max,         0.90, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 },  new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, 97)]
        [InlineData(RepresentationKind.Max,         0.99, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 },  new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, double.NaN)]

        [InlineData(RepresentationKind.Mean,        0.90, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 },  new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, 12)]
        [InlineData(RepresentationKind.Mean,        0.99, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 },  new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, double.NaN)]

        [InlineData(RepresentationKind.MeanPolarDeg,     0.90, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 },  new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, 9.25)]
        [InlineData(RepresentationKind.MeanPolarDeg,     0.99, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 },  new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, double.NaN)]

        [InlineData(RepresentationKind.Sum,         0.90, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 },  new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, 132)]
        [InlineData(RepresentationKind.Sum,         0.99, new int[] { 0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13 },  new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, double.NaN)]

        [InlineData(RepresentationKind.MinBitwise,  0.90, new int[] { 2, 2, 2, 3, 2, 3, 65, 2, 98, 14 },        new byte[] { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, 2)]
        [InlineData(RepresentationKind.MinBitwise,  0.99, new int[] { 2, 2, 2, 3, 2, 3, 65, 2, 98, 14 },        new byte[] { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, double.NaN)]

        [InlineData(RepresentationKind.MaxBitwise,  0.90, new int[] { 2, 2, 2, 3, 2, 3, 65, 2, 98, 14 },        new byte[] { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, 111)]
        [InlineData(RepresentationKind.MaxBitwise,  0.99, new int[] { 2, 2, 2, 3, 2, 3, 65, 2, 98, 14 },        new byte[] { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, double.NaN)]

        [Theory]
        public void CanAggregateSingle(RepresentationKind kind, double nanThreshold, int[] data, byte[] status, double expected)
        {
            // Arrange
            var options = Options.Create(new DataOptions() { AggregationNaNThreshold = nanThreshold });
            var processingService = new ProcessingService(options);
            var blockSize = data.Length;
            var actual = new double[1];
            var byteData = MemoryMarshal.AsBytes<int>(data).ToArray();

            // Act
            processingService.Process(NexusDataType.INT32, kind, byteData, status, targetBuffer: actual, blockSize);

            // Assert
            Assert.Equal(expected, actual[0], precision: 2);
        }

        [Fact]
        public void CanAggregateMultiple()
        {
            // Arrange
            var data = new int[]
            {
                0, 1, 2, 3, -4, 5, 6, 7, 0, 2, 97, 13,
                0, 1, 2, 3, -4, 5, 6, 7, 3, 2, 87, 12
            };

            var status = new byte[]
            {
                1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1,
                1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
            };

            var expected = new double[] { 132, 123 };
            var options = Options.Create(new DataOptions());
            var processingService = new ProcessingService(options);
            var blockSize = data.Length / 2;
            var actual = new double[expected.Length];
            var byteData = MemoryMarshal.AsBytes<int>(data).ToArray();

            // Act
            processingService.Process(NexusDataType.INT32, RepresentationKind.Sum, byteData, status, targetBuffer: actual, blockSize);

            // Assert
            Assert.True(expected.SequenceEqual(actual));
        }

        [Fact]
        public void CanResample()
        {
            // Arrange
            var data = new float[]
            {
                0, 1, 2, 3
            };

            var status = new byte[]
            {
                1, 1, 0, 1
            };

            var expected = new double[] { 0, 0, 0, 0, 1, 1, 1, 1, double.NaN, double.NaN, double.NaN, double.NaN, 3, 3, 3, 3};
            var options = Options.Create(new DataOptions());
            var processingService = new ProcessingService(options);
            var blockSize = 4;
            var actual = new double[expected.Length];
            var byteData = MemoryMarshal.AsBytes<float>(data).ToArray();

            // Act
            processingService.Process(NexusDataType.FLOAT32, RepresentationKind.Resampled, byteData, status, targetBuffer: actual, blockSize);

            // Assert
            Assert.True(expected.SequenceEqual(actual));
        }
    }
}
