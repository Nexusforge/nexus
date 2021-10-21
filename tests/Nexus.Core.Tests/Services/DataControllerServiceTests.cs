using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Extensions;
using Nexus.Services;
using System;
using System.Collections.Generic;
using System.Linq;
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

            var backendSource = new BackendSource(Type: "Nexus.Builtin.Inmemory", new Uri("A", UriKind.Relative));
            var expectedCatalog = new ResourceCatalog(id: "/A/B/C");

            var catalogState = new CatalogState()
            {
                BackendSourceToCatalogsMap = new Dictionary<BackendSource, ResourceCatalog[]>()
                {
                    [backendSource] = new[] { new ResourceCatalog(id: "/A/B/C") }
                }
            };

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton(new AppState() { CatalogState = catalogState });

            var serviceProvider = serviceCollection.BuildServiceProvider();
            var loggerFactory = Mock.Of<ILoggerFactory>();
            var dataControllerService = new DataControllerService(extensionHive, serviceProvider, default, default, loggerFactory);

            // Act
            var actual = await dataControllerService.GetDataSourceControllerAsync(backendSource, CancellationToken.None);

            // Assert
            var actualCatalogs = await actual.GetCatalogsAsync(CancellationToken.None);

            Assert.Equal(expectedCatalog, actualCatalogs.First());
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
            var dataControllerService = new DataControllerService(extensionHive, default, default, default, loggerFactory);
            var actual = await dataControllerService.GetDataWriterControllerAsync(resourceLocator, exportParameters, CancellationToken.None);

            // Assert
            /* nothing to assert */
        }
    }
}