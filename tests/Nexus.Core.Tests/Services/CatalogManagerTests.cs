using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Services
{
    public class CatalogManagerTests
    {
        delegate bool GobbleReturns(string catalogId, out string catalogMetadata);

        [Fact]
        public async Task CanLoadCatalogs()
        {
            // Arrange

            /* app state */
            var backendSources = new List<BackendSource>()
            {
                new BackendSource(Type: "A", ResourceLocator: new Uri("A", UriKind.Relative), Configuration: default), // source A, path A, catalog A and B
                new BackendSource(Type: "A", ResourceLocator: new Uri("B", UriKind.Relative), Configuration: default), // source A, path B, catalog C
                new BackendSource(Type: "B", ResourceLocator: new Uri("C", UriKind.Relative), Configuration: default), // source B, path C, catalog D
            };

            var userConfig = new UserConfiguration(SecurityOptions.DefaultRootUser, backendSources);

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

            var timeRangeResultA_B = new TimeRangeResponse(new DateTime(2020, 01, 01), new DateTime(2020, 01, 02));
            var timeRangeResultC = new TimeRangeResponse(DateTime.MaxValue, DateTime.MinValue);
            var timeRangeResultD = new TimeRangeResponse(new DateTime(2020, 01, 01), new DateTime(2020, 01, 02));

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
                        .Setup(s => s.GetCatalogIdsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .Returns<string, CancellationToken>((path, cancellationToken) =>
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
              .Setup(databaseManager => databaseManager.EnumerateUserConfigs())
              .Returns(new[] { JsonSerializer.Serialize(userConfig) });

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
            var username = "test";

            var claimsIdentity = new ClaimsIdentity(
                new Claim[] {
                    new Claim(ClaimTypes.Name, username),
                    new Claim(Claims.CAN_EDIT_CATALOG, "^/[A|B]$") 
                },
                "Fake authentication type");

            var principal = new ClaimsPrincipal(claimsIdentity);

            Mock.Get(userManagerWrapper)
               .Setup(userManagerWrapper => userManagerWrapper.GetClaimsPrincipalAsync(It.IsAny<string>()))
               .Returns(Task.FromResult(principal));

            /* logger */
            var logger = Mock.Of<ILogger<CatalogManager>>();

            /* options */
            var optionsValue = new PathsOptions();
            var options = Mock.Of<IOptions<PathsOptions>>();

            Mock.Get(options)
                .SetupGet(s => s.Value)
                .Returns(optionsValue);

            var securityOptions = Options.Create(new SecurityOptions());

            var catalogManager = new CatalogManager(
                dataControllerService, 
                databaseManager, 
                userManagerWrapper,
                securityOptions, 
                logger);

            var expectedCommonCatalogs = new[]
            {
                new ResourceCatalogBuilder(id: "/A")
                    .AddResource(new ResourceBuilder(id: "A").AddRepresentation(new Representation(NexusDataType.INT16, TimeSpan.FromSeconds(1))).Build())
                    .WithDescription("v2")
                    .Build(),

                new ResourceCatalogBuilder(id: "/B")
                    .AddResource(new ResourceBuilder(id: "A").AddRepresentation(new Representation(NexusDataType.INT16, TimeSpan.FromSeconds(1))).Build())
                    .WithDescription("v1")
                    .Build(),
            };

            var expectedUserCatalogs = new[]
            {
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

            // common catalogs
            var commonCatalogInfos = (await Task.WhenAll(state.CatalogContainersMap[CatalogManager.CommonCatalogsKey].Select(catalogContainer
                => catalogContainer.GetCatalogInfoAsync(CancellationToken.None)))).ToArray();

            var actualCommonCatalogs = commonCatalogInfos.Select(catalogInfo => catalogInfo.Catalog);

            foreach (var (actual, expected) in actualCommonCatalogs.Zip(expectedCommonCatalogs))
            {
                var actualJsonString = JsonSerializerHelper.SerializeIntended(actual);
                var expectedJsonString = JsonSerializerHelper.SerializeIntended(expected);

                Assert.Equal(actualJsonString, expectedJsonString);
            }

            Assert.Equal(new DateTime(2020, 01, 01), commonCatalogInfos[0].Begin);
            Assert.Equal(new DateTime(2020, 01, 02), commonCatalogInfos[0].End);

            Assert.Equal(new DateTime(2020, 01, 01), commonCatalogInfos[1].Begin);
            Assert.Equal(new DateTime(2020, 01, 02), commonCatalogInfos[1].End);

            // user catalogs
            var userCatalogInfos = (await Task.WhenAll(state.CatalogContainersMap[username].Select(catalogContainer
                => catalogContainer.GetCatalogInfoAsync(CancellationToken.None)))).ToArray();

            var actualUserCatalogs = userCatalogInfos.Select(catalogInfo => catalogInfo.Catalog);

            foreach (var (actual, expected) in actualUserCatalogs.Zip(expectedUserCatalogs))
            {
                var actualJsonString = JsonSerializerHelper.SerializeIntended(actual);
                var expectedJsonString = JsonSerializerHelper.SerializeIntended(expected);

                Assert.Equal(actualJsonString, expectedJsonString);
            }

            Assert.Equal(DateTime.MaxValue, userCatalogInfos[0].Begin);
            Assert.Equal(DateTime.MinValue, userCatalogInfos[0].End);

            Assert.Equal(new DateTime(2020, 01, 01), userCatalogInfos[1].Begin);
            Assert.Equal(new DateTime(2020, 01, 02), userCatalogInfos[1].End);
        }

        [Fact]
        public async Task CanMergeFromDifferentUsers()
        {
            // Test case:
            // User A, admin,
            //      /  => /A, /A/B
            //      /A => /A/C
            //
            // User B, no admin,
            //      /  => /A (should be ignored), /B, /C
            //
            // Catalogs of User A should become part of the common catalog containers list
            // Catalog "/B" of User B should become part of the common catalog containers list
            // Catalog "/C" of User B should become part of the user catalog containers list

            /* dataControllerService */
            var dataControllerService = Mock.Of<IDataControllerService>();

            Mock.Get(dataControllerService)
                .Setup(s => s.GetDataSourceControllerAsync(It.IsAny<BackendSource>(), It.IsAny<CancellationToken>(), It.IsAny<CatalogCache>()))
                .Returns<BackendSource, CancellationToken, CatalogCache>((backendSource, cancellationToken, catalogCache) =>
                {
                    var dataSourceController = Mock.Of<IDataSourceController>();

                    Mock.Get(dataSourceController)
                        .Setup(s => s.GetCatalogIdsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .Returns<string, CancellationToken>((path, cancellationToken) =>
                        {
                            var type = backendSource.Type;

                            return (type, path) switch
                            {
                                ("A", "/") => Task.FromResult(new[] { "/A", "/A/B" }),
                                ("A", "/A") => Task.FromResult(new[] { "/A/C" }),
                                ("B", "/") => Task.FromResult(new[] { "/A", "/B", "/C" }),
                                _ => throw new Exception("Unsupported combination.")
                            };
                        });

                    return Task.FromResult(dataSourceController);
                });

            /* databaseManager */
            var backendSourceA = new BackendSource(Type: "A", default, default, default);
            var backendSourceB = new BackendSource(Type: "B", default, default, default);

            var userAConfig = new UserConfiguration("UserA", new List<BackendSource>() { backendSourceA });
            var userBConfig = new UserConfiguration("UserA", new List<BackendSource>() { backendSourceB });

            var databaseManager = Mock.Of<IDatabaseManager>();

            Mock.Get(databaseManager)
              .Setup(databaseManager => databaseManager.EnumerateUserConfigs())
              .Returns(new[] { JsonSerializer.Serialize(userAConfig), JsonSerializer.Serialize(userBConfig) });

            Mock.Get(databaseManager)
               .Setup(databaseManager => databaseManager.TryReadCatalogMetadata(
                   It.IsAny<string>(),
                   out It.Ref<string>.IsAny))
               .Returns(new GobbleReturns((string catalogId, out string catalogMetadataString) =>
               {
                   catalogMetadataString = "{}";
                   return true;
               }));

            /* security options */
            var securityOptions = Options.Create(new SecurityOptions());

            /* catalogManager */
            var catalogManager = new CatalogManager(
                dataControllerService,
                databaseManager,
                default,
                securityOptions,
                NullLogger<CatalogManager>.Instance);

            /* user A */
            var usernameA = "UserA";

            var claimsIdentityA = new ClaimsIdentity(
               new Claim[] {
                    new Claim(ClaimTypes.Name, usernameA),
                    new Claim(Claims.IS_ADMIN, "true")
               },
               "Fake authentication type");

            var userA = new ClaimsPrincipal(claimsIdentityA);

            /* user B */
            var usernameB = "UserB";

            var claimsIdentityB = new ClaimsIdentity(
               new Claim[] {
                    new Claim(ClaimTypes.Name, usernameB),
                    new Claim(Claims.CAN_EDIT_CATALOG, "^/B$")
               },
               "Fake authentication type");

            var userB = new ClaimsPrincipal(claimsIdentityB);

            /* state */
            var state = new CatalogState(new Dictionary<string, List<CatalogContainer>>(), default);

            // act
            await catalogManager.LoadCatalogIdsAsync("/", backendSourceA, userA, state, CancellationToken.None);
            await catalogManager.LoadCatalogIdsAsync("/", backendSourceB, userB, state, CancellationToken.None);
            await catalogManager.LoadCatalogIdsAsync("/A", backendSourceA, userA, state, CancellationToken.None);

            // assert
            Assert.Equal(4, state.CatalogContainersMap[CatalogManager.CommonCatalogsKey].Count);
            Assert.Equal(2, state.CatalogContainersMap["UserB"].Count + 1);

            Assert.Contains(
                state.CatalogContainersMap[CatalogManager.CommonCatalogsKey], 
                container => container.Id == "/A" && container.BackendSource == backendSourceA);

            Assert.Contains(
                state.CatalogContainersMap[CatalogManager.CommonCatalogsKey],
                container => container.Id == "/A/B" && container.BackendSource == backendSourceA);

            Assert.Contains(
                state.CatalogContainersMap[CatalogManager.CommonCatalogsKey],
                container => container.Id == "/A/C" && container.BackendSource == backendSourceA);

            Assert.Contains(
                state.CatalogContainersMap[CatalogManager.CommonCatalogsKey],
                container => container.Id == "/B" && container.BackendSource == backendSourceB);

            Assert.Contains(
                state.CatalogContainersMap["UserB"],
                container => container.Id == "/C" && container.BackendSource == backendSourceB);
        }
    }
}
