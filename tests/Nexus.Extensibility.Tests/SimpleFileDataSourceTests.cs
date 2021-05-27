using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Extensibility.Tests
{
    public class SimpleFileDataSourceTests
    {
        private ILogger _logger;

        public SimpleFileDataSourceTests(ITestOutputHelper xunitLogger)
        {
            _logger = new XunitLoggerProvider(xunitLogger).CreateLogger(nameof(SimpleFileDataSourceTests));
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
            var dataSource = new SimpleFileDataSourceTester()
            {
                RootPath = rootPath,
                Logger = _logger,
                Options = null,
            } as IDataSource;

            var projects = await dataSource.GetDataModelAsync(CancellationToken.None);
            var actual = await dataSource.GetAvailabilityAsync(projects.First().Id, begin, end, CancellationToken.None);

            Assert.Equal(expected, actual, precision);
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
            var dataSource = new SimpleFileDataSourceTester()
            {
                RootPath = rootPath,
                Logger = _logger,
                Options = null,
            } as IDataSource;

            var projects = await dataSource.GetDataModelAsync(CancellationToken.None);
            var actual = await dataSource.GetProjectTimeRangeAsync(projects.First().Id, CancellationToken.None);

            Assert.Equal(expectedBegin, actual.Begin);
            Assert.Equal(expectedEnd, actual.End);
        }
    }
}