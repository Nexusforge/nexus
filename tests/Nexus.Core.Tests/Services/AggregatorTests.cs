using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus.Core;
using Nexus.Services;
using Xunit;

namespace Nexus.Tests
{
    public class AggregatorTests
    {
        [InlineData(AggregationMethod.Min, 0.90, new double[] { 0, 1, 2, 3, -4, 5, 6, 7, double.NaN, 2.5, 97, 12.5 }, -4)]
        [InlineData(AggregationMethod.Min, 0.99, new double[] { 0, 1, 2, 3, -4, 5, 6, 7, double.NaN, 2.5, 97, 12.5 }, double.NaN)]

        [InlineData(AggregationMethod.Max, 0.90, new double[] { 0, 1, 2, 3, -4, 5, 6, 7, double.NaN, 2.5, 97, 12.5 }, 97)]
        [InlineData(AggregationMethod.Max, 0.99, new double[] { 0, 1, 2, 3, -4, 5, 6, 7, double.NaN, 2.5, 97, 12.5 }, double.NaN)]

        [InlineData(AggregationMethod.Mean, 0.90, new double[] { 0, 1, 2, 3, -4, 5, 6, 7, double.NaN, 2.5, 97, 12.5 }, 12)]
        [InlineData(AggregationMethod.Mean, 0.99, new double[] { 0, 1, 2, 3, -4, 5, 6, 7, double.NaN, 2.5, 97, 12.5 }, double.NaN)]

        [InlineData(AggregationMethod.MeanPolar, 0.90, new double[] { 0, 1, 2, 3, -4, 5, 6, 7, double.NaN, 2.5, 97, 12.5 }, 9.248975696093401)]
        [InlineData(AggregationMethod.MeanPolar, 0.99, new double[] { 0, 1, 2, 3, -4, 5, 6, 7, double.NaN, 2.5, 97, 12.5 }, double.NaN)]

        [InlineData(AggregationMethod.Sum, 0.90, new double[] { 0, 1, 2, 3, -4, 5, 6, 7, double.NaN, 2.5, 97, 12.5 }, 132)]
        [InlineData(AggregationMethod.Sum, 0.99, new double[] { 0, 1, 2, 3, -4, 5, 6, 7, double.NaN, 2.5, 97, 12.5 }, double.NaN)]

        [Theory]
        public void CanAggregate1(AggregationMethod method, double nanLimit, double[] data, double expected)
        {
            // Arrange
            var loggerFactory = Mock.Of<ILoggerFactory>();
            var aggregator = new AggregationService(NullLogger<AggregationService>.Instance, loggerFactory);
            var kernelSize = data.Length;

            // Act
            var actual = aggregator.ApplyAggregationFunction(method, argument: "360", kernelSize, data, nanLimit, NullLogger.Instance);

            // Assert
            Assert.Equal(expected, actual[0]);
        }

        [InlineData(AggregationMethod.MinBitwise, 0.90, new int[] { 2, 2, 2, 3, 2, 3, 65, 2, 98, 14 }, new byte[] { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, 2)]
        [InlineData(AggregationMethod.MinBitwise, 0.99, new int[] { 2, 2, 2, 3, 2, 3, 65, 2, 98, 14 }, new byte[] { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, double.NaN)]

        [InlineData(AggregationMethod.MaxBitwise, 0.90, new int[] { 2, 2, 2, 3, 2, 3, 65, 2, 98, 14 }, new byte[] { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, 111)]
        [InlineData(AggregationMethod.MaxBitwise, 0.99, new int[] { 2, 2, 2, 3, 2, 3, 65, 2, 98, 14 }, new byte[] { 1, 1, 1, 1, 1, 1, 0, 1, 1, 1 }, double.NaN)]

        [Theory]
        public void CanAggregate2(AggregationMethod method, double nanLimit, int[] data, byte[] status, double expected)
        {
            // Arrange
            var loggerFactory = Mock.Of<ILoggerFactory>();
            var aggregator = new AggregationService(NullLogger<AggregationService>.Instance, loggerFactory);
            var kernelSize = data.Length;

            // Act
            var actual = aggregator.ApplyAggregationFunction<int>(method, argument: "360", kernelSize, data, status, nanLimit, NullLogger.Instance);

            // Assert
            Assert.Equal(expected, actual[0]);
        }
    }
}