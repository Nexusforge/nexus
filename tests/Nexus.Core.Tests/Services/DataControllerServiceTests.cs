using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Models;
using Nexus.Services;
using Nexus.Sources;
using Nexus.Writers;
using System;
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
              .Returns(new InMemory());

            var backendSource = new BackendSource(
                Type: "Nexus.Builtin.InMemory", 
                new Uri("A", UriKind.Relative), 
                Configuration: default,
                Publish: true);

            var expectedCatalog = InMemory.LoadCatalog("/A/B/C");
            var catalogContainer = new CatalogContainer("/A/B/C", default, backendSource, default, default);

            var catalogContainersMap = new CatalogContainersMap()
            {
                [CatalogManager.CommonCatalogsKey] = new List<CatalogContainer>() { catalogContainer }
            };

            var catalogState = new CatalogState(
                catalogContainersMap,
                CatalogCache: new CatalogCache()
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
              .Returns(new Csv());

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