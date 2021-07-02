using Nexus.DataModel;
using System;
using Xunit;

namespace Nexus.Extensibility.Tests
{
    public class DataModelTests
    {
        [Theory]
        [InlineData("1 Hz_mean_polar", "00:00:01")]
        [InlineData("10 Hz", "00:00:00.1")]
        [InlineData("4000 Hz", "00:00:00.00025")]
        [InlineData("15 s", "00:00:15")]
        [InlineData("1 s", "00:00:01")]
        [InlineData("15 ms", "00:00:00.015")]
        [InlineData("1 ms", "00:00:00.001")]
        [InlineData("15 us", "00:00:00.000015")]
        [InlineData("1 us", "00:00:00.000001")]
        [InlineData("200 ns", "00:00:00.0000002")]
        [InlineData("15 ns", "00:00:00")]
        public void CanCreatUnitStrings(string datasetId, string expectedString)
        {
            var expected = TimeSpan.Parse(expectedString);
            var dataset = new Dataset() { Id = datasetId };
            var actual = dataset.GetSamplePeriod();

            Assert.Equal(expected, actual);
        }
    }
}