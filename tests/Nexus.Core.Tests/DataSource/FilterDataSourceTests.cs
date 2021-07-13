using Microsoft.Extensions.Logging;
using Moq;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
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
            var dataSource = new FilterDataSource() as IDataSource;

            var context = new DataSourceContext()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "FILTERDATA")),
                Configuration = new Dictionary<string, string>(),
                Logger = _logger
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

            // act
            var catalogs = await dataSource.GetCatalogsAsync(CancellationToken.None);

            // assert
            var actual = catalogs.First(catalog => catalog.Id == "/IN_MEMORY/FILTERS/SHARED");
            var actualNames = actual.Resources.Select(resource => resource.Name).ToList();
            var actualGroups = actual.Resources.Select(resource => resource.Group).ToList();
            var actualUnits = actual.Resources.Select(resource => resource.Unit).ToList();
            var actualDataTypes = actual.Resources.SelectMany(resource => resource.Representations.Select(representation => representation.DataType)).ToList();

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
            var dataSource = new FilterDataSource() as IDataSource;

            var context = new DataSourceContext()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "FILTERDATA")),
                Configuration = new Dictionary<string, string>(),
                Logger = _logger
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

            var actual = await dataSource.GetTimeRangeAsync("/IN_MEMORY/FILTERS/SHARED", CancellationToken.None);

            Assert.Equal(DateTime.MaxValue, actual.Begin);
            Assert.Equal(DateTime.MinValue, actual.End);
        }

        [Fact]
        public async Task CanProvideAvailability()
        {
            var dataSource = new FilterDataSource() as IDataSource;

            var context = new DataSourceContext()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "FILTERDATA")),
                Configuration = new Dictionary<string, string>(),
                Logger = _logger
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

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

            var representation = new Representation() { Id = "1 Hz", DataType = NexusDataType.FLOAT64 };

            var resource = new Resource() { Id = Guid.NewGuid() };
            resource.Representations.Add(representation);

            var catalog = new ResourceCatalog() { Id = "/IN_MEMORY/TEST/ACCESSIBLE" };
            catalog.Resources.Add(resource);

            Mock.Get(database)
                .Setup(s => s.Find(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new CatalogItem(catalog, resource, representation));

            // setup data source
            var subDataSource = Mock.Of<IDataSource>();

            Mock.Get(subDataSource)
                .Setup(s => s.ReadAsync(
                    It.IsAny<DateTime>(), 
                    It.IsAny<DateTime>(),
                    It.IsAny<ReadRequest[]>(),
                    It.IsAny<IProgress<double>>(),
                    It.IsAny<CancellationToken>())
                )
                .Callback<DateTime, DateTime, ReadRequest[], IProgress<double>, CancellationToken>((begin, end, requests, progress, cancellationToken) =>
                {
                    var (resourcePath, data, status) = requests[0];

                    var doubleData = data.Cast<double>();
                    doubleData.Span[0] = 2;
                    doubleData.Span[1] = 1;
                    doubleData.Span[2] = 99.99;

                    status.Span[0] = 1;
                    status.Span[1] = 0;
                    status.Span[2] = 1;
                })
                .Returns(Task.CompletedTask);

            // go
            var dataSource = new FilterDataSource()
            {
                IsCatalogAccessible = _ => true,
                Database = database,
                GetDataSourceAsync = id => Task.FromResult(new DataSourceController(subDataSource, null, null))
            } as IDataSource;

            var context = new DataSourceContext()
            {
                ResourceLocator = new Uri(Path.Combine(Directory.GetCurrentDirectory(), "FILTERDATA")),
                Configuration = new Dictionary<string, string>(),
                Logger = _logger
            };

            await dataSource.SetContextAsync(context, CancellationToken.None);

            var catalogs = await dataSource.GetCatalogsAsync(CancellationToken.None);
            var catalog2 = catalogs.First();
            var resource2 = catalog2.Resources.First();
            var representation2 = resource2.Representations.First();
            var catalogItem = new CatalogItem(catalog2, resource2, representation2);

            var begin = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2020, 01, 02, 0, 0, 0, DateTimeKind.Utc);
            var (data, status) = ExtensibilityUtilities.CreateBuffers(representation, begin, end);

            var request = new ReadRequest(catalogItem, data, status);
            await dataSource.ReadAsync(begin, end, new[] { request }, new Progress<double>(), CancellationToken.None);
            var doubleData = data.Cast<double>();

            Assert.Equal(Math.Pow(2, 2), doubleData.Span[0]);
            Assert.Equal(double.NaN, doubleData.Span[1]);
            Assert.Equal(Math.Pow(99.99, 2), doubleData.Span[2]);
            Assert.Equal(double.NaN, doubleData.Span[3]);
        }
    }
}