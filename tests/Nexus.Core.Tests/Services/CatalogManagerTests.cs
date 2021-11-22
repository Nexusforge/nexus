using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Services
{
    public class CatalogManagerTests
    {
        delegate bool GobbleReturns(string catalogId, out string catalogMetadata);

        [Fact]
        public async Task LoadCatalogs()
        {
            // Arrange

            /* app state */
            var backendSources = new List<BackendSource>()
            {
                new BackendSource(Type: "A", ResourceLocator: new Uri("A", UriKind.Relative), Configuration: default), // source A, path A, catalog A and B
                new BackendSource(Type: "A", ResourceLocator: new Uri("B", UriKind.Relative), Configuration: default), // source A, path B, catalog C
                new BackendSource(Type: "B", ResourceLocator: new Uri("C", UriKind.Relative), Configuration: default), // source B, path C, catalog D
            };

            var appState = new AppState()
            {
                Project = new NexusProject(null, backendSources)
            };

            /* dataControllerService */
            var catalogsA_B = new ResourceCatalog[]
            {
                new ResourceCatalogBuilder(id: "/A")
                    .AddResource(new ResourceBuilder(id: "A").AddRepresentation(new Representation(NexusDataType.INT16, TimeSpan.FromSeconds(1))).Build())
                    .WithDescription("v1")
                    .Build(),

                new ResourceCatalogBuilder(id: "/B")
                    .AddResource(new ResourceBuilder(id: "A").AddRepresentation(new Representation(NexusDataType.INT16, TimeSpan.FromSeconds(1))).Build())
                    .WithDescription("v1")
                    .Build()
            };

            var catalogsC = new ResourceCatalog[]
            {
                new ResourceCatalogBuilder(id: "/C")
                    .AddResource(new ResourceBuilder(id: "A").AddRepresentation(new Representation(NexusDataType.INT16, TimeSpan.FromSeconds(60))).Build())
                    .Build()
            };

            var catalogsD = new ResourceCatalog[]
            {
                new ResourceCatalogBuilder(id: "/D")
                    .AddResource(new ResourceBuilder(id: "A").AddRepresentation(new Representation(NexusDataType.INT16, TimeSpan.FromSeconds(1))).Build())
                    .Build()
            };

            var timeRangeResultA_B = new TimeRangeResult(new DateTime(2020, 01, 01), new DateTime(2020, 01, 02));
            var timeRangeResultC = new TimeRangeResult(DateTime.MaxValue, DateTime.MinValue);
            var timeRangeResultD = new TimeRangeResult(new DateTime(2020, 01, 01), new DateTime(2020, 01, 02));

            var dataControllerService = Mock.Of<IDataControllerService>();

            Mock.Get(dataControllerService)
                .Setup(s => s.GetDataSourceControllerAsync(It.IsAny<BackendSource>(), It.IsAny<CancellationToken>(), It.IsAny<CatalogCache>()))
                .Returns<BackendSource, CancellationToken, CatalogCache>((backendSource, cancellationToken, catalogCache) =>
                {
                    var dataSourceController = Mock.Of<IDataSourceController>();

                    var (catalogs, timeRangeResult) = backendSource switch
                    {
                        ("A", _, _, _) a when a.ResourceLocator.OriginalString == "A" => (catalogsA_B, timeRangeResultA_B),
                        ("A", _, _, _) b when b.ResourceLocator.OriginalString == "B" => (catalogsC, timeRangeResultC),
                        ("B", _, _, _) d when d.ResourceLocator.OriginalString == "C" => (catalogsD, timeRangeResultD),
                        _                                                             => (new ResourceCatalog[0], default)
                    };

                    Mock.Get(dataSourceController)
                        .Setup(s => s.GetCatalogIdsAsync(It.IsAny<CancellationToken>()))
                        .Returns<CancellationToken>((cancellationToken) =>
                        {
                            return Task.FromResult(catalogs.Select(catalog => catalog.Id).ToArray());
                        });

                    Mock.Get(dataSourceController)
                        .Setup(s => s.GetCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .Returns<string, CancellationToken>((catalogId, cancellationToken) =>
                        {
                            return Task.FromResult(catalogs.First(catalog => catalog.Id == catalogId));
                        });

                    Mock.Get(dataSourceController)
                        .Setup(s => s.GetTimeRangeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .Returns<string, CancellationToken>((catalogId, cancellationToken) =>
                        {
                            return Task.FromResult(timeRangeResult);
                        });

                    return Task.FromResult(dataSourceController);
                });

            /* databaseManager */
            var catalogMetadata = new CatalogMetadata()
            {
                Overrides = new ResourceCatalogBuilder(id: "/A")
                    .WithDescription("v2")
                    .Build()
            };

            var databaseManager = Mock.Of<IDatabaseManager>();

            Mock.Get(databaseManager)
               .Setup(databaseManager => databaseManager.TryReadCatalogMetadata(
                   It.IsAny<string>(),
                   out It.Ref<string>.IsAny))
               .Returns(new GobbleReturns((string catalogId, out string catalogMetadataString) =>
               {
                   if (catalogId == "/A")
                   {
                       catalogMetadataString = JsonSerializerHelper.SerializeIntended(catalogMetadata);
                       return true;
                   }

                   else
                   {
                       catalogMetadataString = null;
                       return false;
                   }
               }));

            /* userManagerWrapper */
            var userManagerWrapper = Mock.Of<IUserManagerWrapper>();

            Mock.Get(userManagerWrapper)
               .Setup(userManagerWrapper => userManagerWrapper.GetClaimsPrincipalAsync(It.IsAny<string>()))
               .Returns(Task.FromResult(new ClaimsPrincipal()));

            /* logger */
            var logger = Mock.Of<ILogger<CatalogManager>>();

            /* options */
            var optionsValue = new PathsOptions();
            var options = Mock.Of<IOptions<PathsOptions>>();

            Mock.Get(options)
                .SetupGet(s => s.Value)
                .Returns(optionsValue);

            var catalogManager = new CatalogManager(appState, dataControllerService, databaseManager, userManagerWrapper, logger, options);

            var expectedCatalogs = new[]
            {
                new ResourceCatalogBuilder(id: "/A")
                    .AddResource(new ResourceBuilder(id: "A").AddRepresentation(new Representation(NexusDataType.INT16, TimeSpan.FromSeconds(1))).Build())
                    .WithDescription("v2")
                    .Build(),

                new ResourceCatalogBuilder(id: "/B")
                    .AddResource(new ResourceBuilder(id: "A").AddRepresentation(new Representation(NexusDataType.INT16, TimeSpan.FromSeconds(1))).Build())
                    .WithDescription("v1")
                    .Build(),

                new ResourceCatalogBuilder(id: "/C")
                    .AddResource(new ResourceBuilder(id: "A").AddRepresentation(new Representation(NexusDataType.INT16, TimeSpan.FromSeconds(60))).Build())
                    .Build(),

                new ResourceCatalogBuilder(id: "/D")
                    .AddResource(new ResourceBuilder(id: "A").AddRepresentation(new Representation(NexusDataType.INT16, TimeSpan.FromSeconds(1))).Build())
                    .Build()
            };

            // Act
            var state = await catalogManager.CreateCatalogStateAsync(CancellationToken.None);

            // Assert
            var catalogInfos = (await Task.WhenAll(state.CatalogContainers.Select(catalogContainer
                => catalogContainer.GetCatalogInfoAsync(CancellationToken.None)))).ToArray();

            var actualCatalogs = catalogInfos.Select(catalogInfo => catalogInfo.Catalog);

            foreach (var (actual, expected) in actualCatalogs.Zip(expectedCatalogs))
            {
                var actualJsonString = JsonSerializerHelper.SerializeIntended(actual);
                var expectedJsonString = JsonSerializerHelper.SerializeIntended(expected);

                Assert.Equal(actualJsonString, expectedJsonString);
            }

            Assert.Equal(new DateTime(2020, 01, 01), catalogInfos[0].Begin);
            Assert.Equal(new DateTime(2020, 01, 02), catalogInfos[0].End);

            Assert.Equal(new DateTime(2020, 01, 01), catalogInfos[1].Begin);
            Assert.Equal(new DateTime(2020, 01, 02), catalogInfos[1].End);

            Assert.Equal(DateTime.MaxValue, catalogInfos[2].Begin);
            Assert.Equal(DateTime.MinValue, catalogInfos[2].End);

            Assert.Equal(new DateTime(2020, 01, 01), catalogInfos[3].Begin);
            Assert.Equal(new DateTime(2020, 01, 02), catalogInfos[3].End);
        }
    }
}
