using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Services
{
    public class DataControllerServiceTests
    {
        [Fact]
        public async Task CanCreateAndInitializeDataSourceController()
        {
            // Arrange
            var extensionHive = Mock.Of<IExtensionHive>();

            Mock.Get(extensionHive)
              .Setup(extensionHive => extensionHive.GetInstance<IDataSource>(It.IsAny<string>()))
              .Returns(new InMemoryDataSource());

            var backendSource = new BackendSource(Type: "Nexus.Builtin.InMemory", new Uri("A", UriKind.Relative));
            var expectedCatalog = InMemoryDataSource.LoadCatalog("/A/B/C");

            var catalogState = new CatalogState(
                default,
                default,
                BackendSourceToCatalogIdsMap: new Dictionary<BackendSource, string[]>()
                {
                    [backendSource] = new[] { "/A/B/C" }
                },
                BackendSourceCache: new ConcurrentDictionary<BackendSource, ConcurrentDictionary<string, ResourceCatalog>>()
            );

            var appState = new AppState() { CatalogState = catalogState };
            var serviceCollection = new ServiceCollection();
            var serviceProvider = serviceCollection.BuildServiceProvider();

            var loggerFactory = Mock.Of<ILoggerFactory>();

            Mock.Get(loggerFactory)
              .Setup(loggerFactory => loggerFactory.CreateLogger(It.IsAny<string>()))
              .Returns(NullLogger.Instance);

            var dataControllerService = new DataControllerService(appState, extensionHive, default, loggerFactory);

            // Act
            var actual = await dataControllerService.GetDataSourceControllerAsync(backendSource, CancellationToken.None);

            // Assert
            var actualCatalog = await actual.GetCatalogAsync("/A/B/C", CancellationToken.None);

            Assert.Equal(expectedCatalog.Id, actualCatalog.Id);
        }

        [Fact]
        public async Task CanCreateAndInitializeDataWriterController()
        {
            // Arrange
            var extensionHive = Mock.Of<IExtensionHive>();

            Mock.Get(extensionHive)
              .Setup(extensionHive => extensionHive.GetInstance<IDataWriter>(It.IsAny<string>()))
              .Returns(new CsvDataWriter());

            var loggerFactory = Mock.Of<ILoggerFactory>();
            var resourceLocator = new Uri("A", UriKind.Relative);
            var exportParameters = new ExportParameters();

            // Act
            var dataControllerService = new DataControllerService(new AppState(), extensionHive, default, loggerFactory);
            var actual = await dataControllerService.GetDataWriterControllerAsync(resourceLocator, exportParameters, CancellationToken.None);

            // Assert
            /* nothing to assert */
        }
    }
}