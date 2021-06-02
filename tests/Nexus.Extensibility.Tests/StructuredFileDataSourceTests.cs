using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Extensibility.Tests
{
    public class StructuredFileDataSourceTests
    {
        private ILogger _logger;

        public StructuredFileDataSourceTests(ITestOutputHelper xunitLogger)
        {
            _logger = new XunitLoggerProvider(xunitLogger).CreateLogger(nameof(StructuredFileDataSourceTests));
        }

        [Theory]
        [InlineData("DATABASES/A", "2020-01-02", "2020-01-03", 2 / 144.0, 4)]
        [InlineData("DATABASES/A", "2019-12-30", "2020-01-03", 3 / (4 * 144.0), 4)]
        [InlineData("DATABASES/B", "2020-01-02", "2020-01-03", 2 / 144.0, 4)]
        [InlineData("DATABASES/C", "2020-01-02", "2020-01-03", 2 / 144.0, 4)]
        [InlineData("DATABASES/D", "2020-01-02", "2020-01-03", (2 / 144.0 + 2 / 24.0) / 2, 4)]
        [InlineData("DATABASES/E", "2020-01-02", "2020-01-03", (1 + 2 / 48.0) / 2, 4)]
        [InlineData("DATABASES/F", "2020-01-02", "2020-01-03", 2 / 24.0, 4)]
        [InlineData("DATABASES/G", "2020-01-01", "2020-01-02", 2 / 86400.0, 6)]
        [InlineData("DATABASES/H", "2020-01-02", "2020-01-03", 2 / 144.0, 4)]
        public async Task CanProvideAvailability(string rootPath, DateTime begin, DateTime end, double expected, int precision)
        {
            var dataSource = new StructuredFileDataSourceTester()
            {
                RootPath = rootPath,
                Logger = _logger,
                Parameters = null,
            } as IDataSource;

            await dataSource.OnParametersSetAsync();

            var projects = await dataSource.GetDataModelAsync(CancellationToken.None);
            var actual = await dataSource.GetAvailabilityAsync(projects.First().Id, begin, end, CancellationToken.None);

            Assert.Equal(expected, actual, precision);
        }

        [Theory]
        [InlineData("2020-01-01T00-00-00Z", "2020-01-01T00-00-00Z")]
        [InlineData("2020-01-02T00-00-00Z", "2020-01-01T00-00-00Z")]
        public async Task GetAvailabilityThrowsForInvalidTimePeriod(string beginString, string endString)
        {
            var begin = DateTime.ParseExact(beginString, "yyyy-MM-ddTHH-mm-ssZ", default);
            var end = DateTime.ParseExact(endString, "yyyy-MM-ddTHH-mm-ssZ", default);

            var dataSource = new StructuredFileDataSourceTester()
            {
                RootPath = string.Empty,
                Logger = _logger,
                Parameters = null,
            } as IDataSource;
           
            await Assert.ThrowsAsync<ArgumentException>(() => 
                dataSource.GetAvailabilityAsync("/A/B/C", begin, end, CancellationToken.None));
        }

        [Theory]
        [InlineData("DATABASES/A", "2019-12-31", "2020-01-02")]
        [InlineData("DATABASES/B", "2019-12-31", "2020-01-02")]
        [InlineData("DATABASES/C", "2019-12-31", "2020-01-02")]
        [InlineData("DATABASES/D", "2019-12-31", "2020-01-02")]
        [InlineData("DATABASES/E", "2019-12-31", "2020-01-02")]
        [InlineData("DATABASES/F", "2019-12-31", "2020-01-02")]
        [InlineData("DATABASES/G", "2019-12-31", "2020-01-01")]
        [InlineData("DATABASES/H", "2019-12-31", "2020-01-02")]
        public async Task CanProvideProjectTimeRange(string rootPath, DateTime expectedBegin, DateTime expectedEnd)
        {
            var dataSource = new StructuredFileDataSourceTester()
            {
                RootPath = rootPath,
                Logger = _logger,
                Parameters = null,
            } as IDataSource;

            await dataSource.OnParametersSetAsync();

            var projects = await dataSource.GetDataModelAsync(CancellationToken.None);
            var actual = await dataSource.GetProjectTimeRangeAsync(projects.First().Id, CancellationToken.None);

            Assert.Equal(expectedBegin, actual.Begin);
            Assert.Equal(expectedEnd, actual.End);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CanReadSingle(bool overrideFindFilePathsWithNoDateTime)
        {
            var dataSource = new StructuredFileDataSourceTester(overrideFindFilePathsWithNoDateTime)
            {
                RootPath = "DATABASES/TESTDATA",
                Logger = _logger,
                Parameters = null,
            } as IDataSource;

            var dataModel = await dataSource.GetDataModelAsync(CancellationToken.None);
            var dataset = dataModel.First().Channels.First().Datasets.First();

            var begin = new DateTime(2019, 12, 31, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 03, 0, 0, 0, DateTimeKind.Utc);
            var readResult = ExtensibilityUtilities.CreateReadResult<long>(dataset, begin, end);

            var expectedLength = 3 * 86400;
            var expectedData = new long[expectedLength];
            var expectedStatus = new byte[expectedLength];

            void GenerateData(DateTimeOffset dateTime)
            {
                var data = Enumerable.Range(0, 600)
                    .Select(value => dateTime.Add(TimeSpan.FromSeconds(value)).ToUnixTimeSeconds())
                    .ToArray();

                var offset = (int)(dateTime - begin).TotalSeconds;
                data.CopyTo(expectedData.AsSpan().Slice(offset));
                expectedStatus.AsSpan().Slice(offset, 600).Fill(1);
            }

            GenerateData(new DateTimeOffset(2019, 12, 31, 12, 00, 0, 0, TimeSpan.Zero));
            GenerateData(new DateTimeOffset(2019, 12, 31, 12, 20, 0, 0, TimeSpan.Zero));
            GenerateData(new DateTimeOffset(2020, 01, 01, 00, 00, 0, 0, TimeSpan.Zero));
            GenerateData(new DateTimeOffset(2020, 01, 02, 09, 40, 0, 0, TimeSpan.Zero));
            GenerateData(new DateTimeOffset(2020, 01, 02, 09, 50, 0, 0, TimeSpan.Zero));

            await dataSource.OnParametersSetAsync();
            await dataSource.ReadSingleAsync(dataset, readResult, begin, end, CancellationToken.None);

            Assert.Equal(expectedLength, readResult.Length);
            Assert.True(expectedData.SequenceEqual(readResult.Data.ToArray()));
            Assert.True(expectedStatus.SequenceEqual(readResult.Status.ToArray()));
        }

        [Theory]
        [InlineData("2020-01-01T00-00-00Z", "2020-01-01T00-00-00Z")]
        [InlineData("2020-01-02T00-00-00Z", "2020-01-01T00-00-00Z")]
        public async Task ReadSingleThrowsForInvalidTimePeriod(string beginString, string endString)
        {
            var begin = DateTime.ParseExact(beginString, "yyyy-MM-ddTHH-mm-ssZ", default);
            var end = DateTime.ParseExact(endString, "yyyy-MM-ddTHH-mm-ssZ", default);

            var dataSource = new StructuredFileDataSourceTester()
            {
                RootPath = string.Empty,
                Logger = _logger,
                Parameters = null,
            } as IDataSource;

            await Assert.ThrowsAsync<ArgumentException>(() =>
                dataSource.ReadSingleAsync<byte>(default, default, begin, end, CancellationToken.None));
        }
    }
}