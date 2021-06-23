using Microsoft.Extensions.Logging;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Core.Tests
{
    public class InMemoryDataSourceTests
    {
        private ILogger _logger;

        public InMemoryDataSourceTests(ITestOutputHelper xunitLogger)
        {
            _logger = new XunitLoggerProvider(xunitLogger).CreateLogger(nameof(InMemoryDataSourceTests));
        }

        [Fact]
        public async Task ProvidesCatalog()
        {
            // arrange
            var dataSource = new InMemoryDataSource()
            {
                ResourceLocator = new Uri("memory://localhost"),
                Logger = _logger,
                Configuration = new Dictionary<string, string>()
            } as IDataSource;

            await dataSource.OnParametersSetAsync();

            // act
            var catalogs = await dataSource.GetCatalogsAsync(CancellationToken.None);

            // assert
            var actual = catalogs.First(catalog => catalog.Id == "/IN_MEMORY/TEST/ACCESSIBLE");
            var actualNames = actual.Channels.Select(channel => channel.Name).ToList();
            var actualGroups = actual.Channels.Select(channel => channel.Group).ToList();
            var actualUnits = actual.Channels.Select(channel => channel.Unit).ToList();
            var actualDataTypes = actual.Channels.SelectMany(channel => channel.Datasets.Select(dataset => dataset.DataType)).ToList();

            var expectedNames = new List<string>() { "T1", "V1", "unix_time1", "unix_time2" };
            var expectedGroups = new List<string>() { "Group 1", "Group 1", "Group 2", "Group 2" };
            var expectedUnits = new List<string>() { "°C", "m/s", "", "" };
            var expectedDataTypes = new List<NexusDataType>() { NexusDataType.FLOAT64, NexusDataType.FLOAT64, NexusDataType.INT32, NexusDataType.FLOAT64, NexusDataType.FLOAT64 };

            Assert.True(expectedNames.SequenceEqual(actualNames));
            Assert.True(expectedGroups.SequenceEqual(actualGroups));
            Assert.True(expectedUnits.SequenceEqual(actualUnits));
            Assert.True(expectedDataTypes.SequenceEqual(actualDataTypes));
        }

        [Fact]
        public async Task CanProvideTimeRange()
        {
            var dataSource = new InMemoryDataSource()
            {
                ResourceLocator = new Uri("memory://localhost"),
                Logger = _logger,
                Configuration = new Dictionary<string, string>()
            } as IDataSource;

            await dataSource.OnParametersSetAsync();

            var actual = await dataSource.GetTimeRangeAsync("/IN_MEMORY/TEST/ACCESSIBLE", CancellationToken.None);

            Assert.Equal(DateTime.MinValue, actual.Begin);
            Assert.Equal(DateTime.MaxValue, actual.End);
        }

        [Fact]
        public async Task CanProvideAvailability()
        {
            var dataSource = new InMemoryDataSource()
            {
                ResourceLocator = new Uri("memory://localhost"),
                Logger = _logger,
                Configuration = new Dictionary<string, string>()
            } as IDataSource;

            await dataSource.OnParametersSetAsync();

            var begin = new DateTime(2020, 01, 02, 00, 00, 00, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 03, 00, 00, 00, DateTimeKind.Utc);
            var expected = new Random((int)begin.Ticks).NextDouble() / 10 + 0.9;
            var actual = await dataSource.GetAvailabilityAsync("/A/B/C", begin, end, CancellationToken.None);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task CanReadFullDay()
        {
            var dataSource = new InMemoryDataSource()
            {
                ResourceLocator = new Uri("memory://localhost"),
                Logger = _logger,
                Configuration = new Dictionary<string, string>()
            } as IDataSource;

            await dataSource.OnParametersSetAsync();

            var catalogs = await dataSource.GetCatalogsAsync(CancellationToken.None);
            var dataset = catalogs.First().Channels.First().Datasets.First();

            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 02, 0, 0, 0, DateTimeKind.Utc);
            var result = ExtensibilityUtilities.CreateReadResult(dataset, begin, end);

            await dataSource.ReadSingleAsync(dataset.GetPath(), result, begin, end, CancellationToken.None);
            var doubleData = result.GetData<double>();

            Assert.Equal(-0.059998, doubleData.Span[0], precision: 6);
            Assert.Equal(0.427089, doubleData.Span[29], precision: 6);
            Assert.Equal(0.607610, doubleData.Span[54], precision: 6);
        }
    }
}