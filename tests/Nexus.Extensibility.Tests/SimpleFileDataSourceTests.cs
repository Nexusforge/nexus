using Microsoft.Extensions.Logging;
using System;
using System.Linq;
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
        [InlineData("DATABASES/A", "2020-01-02", 2 / 144.0, 4)]
        [InlineData("DATABASES/B", "2020-01-02", 2 / 144.0, 4)]
        [InlineData("DATABASES/C", "2020-01-02", 2 / 144.0, 4)]
        [InlineData("DATABASES/D", "2020-01-02", (2 / 144.0 + 2 / 24.0) / 2, 4)]
        [InlineData("DATABASES/E", "2020-01-02", (1 + 2 / 48.0) / 2, 4)]
        [InlineData("DATABASES/F", "2020-01-02", 2 / 24.0, 4)]
        [InlineData("DATABASES/G", "2020-01-01", 2 / 86400.0, 6)]
        public async Task CanProvideAvailability(string rootPath, DateTime day, double expected, int precision)
        {
            var dataSource = new SimpleFileDataSourceTester()
            {
                RootPath = rootPath,
                Logger = _logger,
                Options = null,
            };

            var projects = await dataSource.InitializeAsync();
            var actual = await dataSource.GetAvailabilityAsync(projects.First().Id, day);

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
        public async Task CanAssignProjectLifetime(string rootPath, DateTime expectedStart, DateTime expectedEnd)
        {
            var dataSource = new SimpleFileDataSourceTester()
            {
                RootPath = rootPath,
                Logger = _logger,
                Options = null,
            };

            var projects = await dataSource.InitializeAsync();
            var project = projects.First();

            Assert.Equal(expectedStart, project.ProjectStart);
            Assert.Equal(expectedEnd, project.ProjectEnd);
        }
    }
}