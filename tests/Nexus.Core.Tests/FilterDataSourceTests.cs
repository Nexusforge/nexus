using Microsoft.Extensions.Logging;
using Moq;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Nexus.Core.Tests
{
    public class FilterDataSourceTests
    {
        private ILogger _logger;

        public FilterDataSourceTests(ITestOutputHelper xunitLogger)
        {
            _logger = new XunitLoggerProvider(xunitLogger).CreateLogger(nameof(InMemoryDataSourceTests));
        }

        [Fact]
        public async Task ProvidesCatalog()
        {
            // arrange
            var dataSource = new FilterDataSource()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "FILTERDATA")),
                Logger = _logger,
                Parameters = new Dictionary<string, string>()
            } as IDataSource;

            await dataSource.OnParametersSetAsync();

            // act
            var catalogs = await dataSource.GetCatalogsAsync(CancellationToken.None);

            // assert
            var actual = catalogs.First(catalog => catalog.Id == "/IN_MEMORY/FILTERS/SHARED");
            var actualNames = actual.Channels.Select(channel => channel.Name).ToList();
            var actualGroups = actual.Channels.Select(channel => channel.Group).ToList();
            var actualUnits = actual.Channels.Select(channel => channel.Unit).ToList();
            var actualDataTypes = actual.Channels.SelectMany(channel => channel.Datasets.Select(dataset => dataset.DataType)).ToList();

            var expectedNames = new List<string>() { "T1_squared" };
            var expectedGroups = new List<string>() { "test" };
            var expectedUnits = new List<string>() { "°C²" };
            var expectedDataTypes = new List<NexusDataType>() { NexusDataType.FLOAT64 };

            Assert.True(expectedNames.SequenceEqual(actualNames));
            Assert.True(expectedGroups.SequenceEqual(actualGroups));
            Assert.True(expectedUnits.SequenceEqual(actualUnits));
            Assert.True(expectedDataTypes.SequenceEqual(actualDataTypes));
        }

        [Fact]
        public async Task CanProvideTimeRange()
        {
            var dataSource = new FilterDataSource()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "FILTERDATA")),
                Logger = _logger,
                Parameters = new Dictionary<string, string>()
            } as IDataSource;

            await dataSource.OnParametersSetAsync();

            var actual = await dataSource.GetTimeRangeAsync("/IN_MEMORY/FILTERS/SHARED", CancellationToken.None);

            Assert.Equal(DateTime.MaxValue, actual.Begin);
            Assert.Equal(DateTime.MinValue, actual.End);
        }

        [Fact]
        public async Task CanProvideAvailability()
        {
            var dataSource = new FilterDataSource()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "FILTERDATA")),
                Logger = _logger,
                Parameters = new Dictionary<string, string>()
            } as IDataSource;

            await dataSource.OnParametersSetAsync();

            var begin = new DateTime(2020, 01, 01, 00, 00, 00, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 02, 00, 00, 00, DateTimeKind.Utc);
            var actual = await dataSource.GetAvailabilityAsync("/IN_MEMORY/FILTERS/SHARED", begin, end, CancellationToken.None);

            Assert.Equal(1.0, actual);
        }

        [Fact]
        public async Task CanReadFullDay()
        {
            // setup database
            var database = Mock.Of<INexusDatabase>(x => x.CatalogContainers == new List<CatalogContainer>()
            {
                new CatalogContainer("/IN_MEMORY/TEST/ACCESSIBLE")
            });

            var catalog = new Catalog("/IN_MEMORY/TEST/ACCESSIBLE");
            var channel = new Channel(Guid.NewGuid(), catalog);
            var dataset = new Dataset("1 Hz", channel) { DataType = NexusDataType.FLOAT64 };

            Mock.Get(database)
                .Setup(s => s.TryFindDatasetById(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), out dataset))
                .Returns(true);

            // setup data source
            var subDataSource = Mock.Of<IDataSource>();

            Mock.Get(subDataSource)
                .Setup(s => s.ReadSingleAsync(
                    It.IsAny<Dataset>(), 
                    It.IsAny<ReadResult>(),
                    It.IsAny<DateTime>(), 
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>())
                )
                .Callback<Dataset, ReadResult, DateTime, DateTime, CancellationToken>((dataset, result, begin, end, cancellationToken) =>
                {
                    var data = result.GetData<double>();
                    data.Span[0] = 2;
                    data.Span[1] = 1;
                    data.Span[2] = 99.99;

                    result.Status.Span[0] = 1;
                    result.Status.Span[1] = 0;
                    result.Status.Span[2] = 1;
                })
                .Returns(Task.CompletedTask);

            // go
            var dataSource = new FilterDataSource()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "FILTERDATA")),
                Logger = _logger,
                Parameters = new Dictionary<string, string>(),
                IsCatalogAccessible = _ => true,
                Database = database,
                GetDataSource = id => new DataSourceController(subDataSource, null)
            } as IDataSource;

            await dataSource.OnParametersSetAsync();

            var catalogs = await dataSource.GetCatalogsAsync(CancellationToken.None);
            dataset = catalogs.First().Channels.First().Datasets.First();

            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 02, 0, 0, 0, DateTimeKind.Utc);
            var result = ExtensibilityUtilities.CreateReadResult(dataset, begin, end);

            await dataSource.ReadSingleAsync(dataset, result, begin, end, CancellationToken.None);
            var doubleData = result.GetData<double>();

            Assert.Equal(Math.Pow(2, 2), doubleData.Span[0]);
            Assert.Equal(double.NaN, doubleData.Span[1]);
            Assert.Equal(Math.Pow(99.99, 2), doubleData.Span[2]);
            Assert.Equal(double.NaN, doubleData.Span[3]);
        }
    }
}