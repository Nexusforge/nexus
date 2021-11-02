using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus;
using Nexus.Core.Tests;
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

namespace DataSource
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
            var actualIds = actual.Resources.Select(resource => resource.Id).ToList();
            var actualUnits = actual.Resources.Select(resource => resource.Properties["Unit"]).ToList();
            var actualGroups = actual.Resources.SelectMany(
                resource => resource.Properties.Where(current => current.Key.StartsWith("Groups"))).Select(current => current.Value).ToList();
            var actualDataTypes = actual.Resources.SelectMany(resource => resource.Representations.Select(representation => representation.DataType)).ToList();

            var expectedIds = new List<string>() { "T1_squared" };
            var expectedUnits = new List<string>() { "°C²" };
            var expectedGroups = new List<string>() { "test" };
            var expectedDataTypes = new List<NexusDataType>() { NexusDataType.FLOAT64 };

            Assert.True(expectedIds.SequenceEqual(actualIds));
            Assert.True(expectedUnits.SequenceEqual(actualUnits));
            Assert.True(expectedGroups.SequenceEqual(actualGroups));
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
            // setup catalog collection
            var representation = new Representation(dataType: NexusDataType.FLOAT64, samplePeriod: TimeSpan.FromSeconds(1), detail: "mean" );

            var resourceBuilder = new ResourceBuilder("T1");
            resourceBuilder.AddRepresentation(representation);

            var catalogBuilder = new ResourceCatalogBuilder(id: "/IN_MEMORY/TEST/ACCESSIBLE");
            catalogBuilder.AddResource(resourceBuilder.Build());

            var catalog = catalogBuilder.Build();

            var catalogCollection = new CatalogCollection(new List<CatalogContainer>()
            {
                new CatalogContainer(DateTime.MinValue, DateTime.MaxValue, catalog, null)
            });

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

                    var doubleData = data.Cast<byte, double>();
                    doubleData.Span[0] = 2;
                    doubleData.Span[1] = 1;
                    doubleData.Span[2] = 99.99;

                    status.Span[0] = 1;
                    status.Span[1] = 0;
                    status.Span[2] = 1;
                })
                .Returns(Task.CompletedTask);

            // go
            var backendSource = new BackendSource(default, default);

            var dataSource = new FilterDataSource()
            {
                DataSourceControllerLogger = NullLogger<DataSourceController>.Instance,
                IsCatalogAccessible = _ => true,
                GetCatalogCollection = () => catalogCollection,
                GetDataSourceControllerAsync = async id =>
                {
                    var controller = new DataSourceController(subDataSource, backendSource, NullLogger.Instance);
                    await controller.InitializeAsync(new[] { catalog }, default);
                    return controller;
                }
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
            var doubleData = data.Cast<byte, double>();

            Assert.Equal(Math.Pow(2, 2), doubleData.Span[0]);
            Assert.Equal(double.NaN, doubleData.Span[1]);
            Assert.Equal(Math.Pow(99.99, 2), doubleData.Span[2]);
            Assert.Equal(double.NaN, doubleData.Span[3]);
        }
    }
}