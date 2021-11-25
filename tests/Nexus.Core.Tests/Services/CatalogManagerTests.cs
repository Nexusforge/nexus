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
        public async Task CanCreateCatalogState()
        {
            // Test case:
            // User A, admin,
            //      /  => /A, /A/B (should be ignored)
            //
            // User B, no admin,
            //      /  => /A (should be ignored), /B, /C/A
            //
            // Catalog "/A"     of User A should become part of the common catalog containers list
            // Catalog "/A/B"   of User A should be ignored
            // Catalog "A"      of user B should be ignored
            // Catalog "/B"     of User B should become part of the common catalog containers list
            // Catalog "/C/A"   of User B should become part of the user catalog containers list

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
                                ("B", "/") => Task.FromResult(new[] { "/A", "/B", "/C/A" }),
                                _ => throw new Exception("Unsupported combination.")
                            };
                        });

                    return Task.FromResult(dataSourceController);
                });

            /* databaseManager */
            var backendSourceA = new BackendSource(Type: "A", new Uri("", UriKind.Relative), new Dictionary<string, string>(), default);
            var backendSourceB = new BackendSource(Type: "B", new Uri("", UriKind.Relative), new Dictionary<string, string>(), default);

            var userAConfig = new UserConfiguration("UserA", new List<BackendSource>() { backendSourceA });
            var userBConfig = new UserConfiguration("UserB", new List<BackendSource>() { backendSourceB });

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

            /* userManagerWrapper */
            var userManagerWrapper = Mock.Of<IUserManagerWrapper>();

            /* => user A */
            var usernameA = "UserA";

            var claimsIdentityA = new ClaimsIdentity(
               new Claim[] {
                    new Claim(ClaimTypes.Name, usernameA),
                    new Claim(Claims.IS_ADMIN, "true")
               },
               "Fake authentication type");

            var userA = new ClaimsPrincipal(claimsIdentityA);

            /* => user B */
            var usernameB = "UserB";

            var claimsIdentityB = new ClaimsIdentity(
               new Claim[] {
                    new Claim(ClaimTypes.Name, usernameB),
                    new Claim(Claims.CAN_EDIT_CATALOG, "^/B$")
               },
               "Fake authentication type");

            var userB = new ClaimsPrincipal(claimsIdentityB);

            Mock.Get(userManagerWrapper)
               .Setup(userManagerWrapper => userManagerWrapper.GetClaimsPrincipalAsync(It.IsAny<string>()))
               .Returns<string>(username =>
               {
                   return username switch
                   {
                       "UserA"  => Task.FromResult(userA),
                       "UserB"  => Task.FromResult(userB),
                       _        => Task.FromResult<ClaimsPrincipal>(null)
                   };
               });

            /* security options */
            var securityOptions = Options.Create(new SecurityOptions());

            /* catalogManager */
            var catalogManager = new CatalogManager(
                dataControllerService,
                databaseManager,
                userManagerWrapper,
                securityOptions,
                NullLogger<CatalogManager>.Instance);

            // act
            var state = await catalogManager.CreateCatalogStateAsync(CancellationToken.None);

            // assert
            Assert.Equal(2, state.CatalogContainersMap[CatalogManager.CommonCatalogsKey].Count);
            Assert.Single(state.CatalogContainersMap["UserB"]);

            Assert.Contains(
                state.CatalogContainersMap[CatalogManager.CommonCatalogsKey],
                container => container.Id == "/A" && container.BackendSource == backendSourceA);

            Assert.Contains(
                state.CatalogContainersMap[CatalogManager.CommonCatalogsKey],
                container => container.Id == "/B" && container.BackendSource == backendSourceB);

            Assert.Contains(
                state.CatalogContainersMap["UserB"],
                container => container.Id == "/C/A" && container.BackendSource == backendSourceB);
        }

        [Fact]
        public async Task CanAttachChildCatalogs()
        {
            // Arrange

            /* data controller service */
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
                            return Task.FromResult(new[] { "/A/B", "/A/C", "/A/B/C" });
                        });

                    return Task.FromResult(dataSourceController);
                });

            /* database manager */
            var databaseManager = Mock.Of<IDatabaseManager>();

            Mock.Get(databaseManager)
               .Setup(databaseManager => databaseManager.TryReadCatalogMetadata(
                   It.IsAny<string>(),
                   out It.Ref<string>.IsAny))
               .Returns(new GobbleReturns((string catalogId, out string catalogMetadataString) =>
               {
                   catalogMetadataString = "{}";
                   return true;
               }));

            /* catalog manager */
            var catalogManager = new CatalogManager(
                dataControllerService,
                databaseManager,
                default,
                Options.Create(new SecurityOptions()),
                NullLogger<CatalogManager>.Instance);

            /* user */
            var username = "User";

            var claimsIdentity = new ClaimsIdentity(
               new Claim[] {
                    new Claim(ClaimTypes.Name, username),
               },
               "Fake authentication type");

            var user = new ClaimsPrincipal(claimsIdentity);

            /* catalog containers map */
            var parent = new CatalogContainer("/A", user, default, default, catalogManager);

            var catalogContainersMap = new CatalogContainersMap()
            {
                ["User"] = new List<CatalogContainer>() { parent }
            };

            // Act
            await catalogManager.AttachChildCatalogIdsAsync(parent, catalogContainersMap, CancellationToken.None);

            // Assert
            Assert.Equal(3, catalogContainersMap[username].Count);

            Assert.Contains(
                catalogContainersMap[username],
                container => container.Id == "/A");

            Assert.Contains(
                catalogContainersMap[username],
                container => container.Id == "/A/B");

            Assert.Contains(
                catalogContainersMap[username],
                container => container.Id == "/A/C");
        }

        [Fact]
        public async Task CanLoadCatalogInfos()
        {
            // Arrange

            /* expected catalogs */
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

            /* expected time range responses */
            var timeRangeResponseA_B = new TimeRangeResponse(new DateTime(2020, 01, 01), new DateTime(2020, 01, 02));
            var timeRangeResponseC = new TimeRangeResponse(DateTime.MaxValue, DateTime.MinValue);
            var timeRangeResponseD = new TimeRangeResponse(new DateTime(2020, 01, 01), new DateTime(2020, 01, 02));

            /* data controller service */
            var dataControllerService = Mock.Of<IDataControllerService>();

            Mock.Get(dataControllerService)
                .Setup(s => s.GetDataSourceControllerAsync(It.IsAny<BackendSource>(), It.IsAny<CancellationToken>(), It.IsAny<CatalogCache>()))
                .Returns<BackendSource, CancellationToken, CatalogCache>((backendSource, cancellationToken, catalogCache) =>
                {
                    var dataSourceController = Mock.Of<IDataSourceController>();

                    var (catalogs, timeRangeResult) = backendSource switch
                    {
                        ("A", _, _, _) a when a.ResourceLocator.OriginalString == "A" => (expectedCommonCatalogs, timeRangeResponseA_B),
                        ("A", _, _, _) b when b.ResourceLocator.OriginalString == "B" => (new ResourceCatalog[] { expectedUserCatalogs[0] }, timeRangeResponseC),
                        ("B", _, _, _) d when d.ResourceLocator.OriginalString == "C" => (new ResourceCatalog[] { expectedUserCatalogs[1] }, timeRangeResponseD),
                        _                                                             => (new ResourceCatalog[0], default)
                    };

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

            /* user */
            var username = "test";

            var claimsIdentity = new ClaimsIdentity(
                new Claim[] {
                    new Claim(ClaimTypes.Name, username),
                    new Claim(Claims.CAN_EDIT_CATALOG, "^/[A|B]$")
                },
                "Fake authentication type");

            var user = new ClaimsPrincipal(claimsIdentity);

            /* catalog metadata */
            var catalogMetadata = new CatalogMetadata()
            {
                Overrides = new ResourceCatalogBuilder(id: "/A")
                    .WithDescription("v2")
                    .Build()
            };

            /* backend sources */
            var backendSourceA_B = new BackendSource(Type: "A", ResourceLocator: new Uri("A", UriKind.Relative), Configuration: default); // source A, path A, catalog A and B
            var backendSourceC = new BackendSource(Type: "A", ResourceLocator: new Uri("B", UriKind.Relative), Configuration: default); // source A, path B, catalog C
            var backendSourceD = new BackendSource(Type: "B", ResourceLocator: new Uri("C", UriKind.Relative), Configuration: default); // source B, path C, catalog D

            /* catalog manager */
            var catalogManager = new CatalogManager(dataControllerService, default, default, Options.Create(new SecurityOptions()), default);

            /* catalog containers */
            var commonCatalogContainers = new List<CatalogContainer>()
            {
                new CatalogContainer("/A", user, backendSourceA_B, catalogMetadata, catalogManager),
                new CatalogContainer("/B", user, backendSourceA_B, default, catalogManager)
            };

            var testCatalogContainers = new List<CatalogContainer>()
            {
                new CatalogContainer("/C", user, backendSourceC, default, catalogManager),
                new CatalogContainer("/D", user, backendSourceD, default, catalogManager)
            };

            var catalogContainersMap = new CatalogContainersMap()
            {
                [""] = commonCatalogContainers,
                ["test"] = testCatalogContainers
            };

            var state = new CatalogState(catalogContainersMap, default);

            // Act
            var commonCatalogInfos = (await Task.WhenAll(state.CatalogContainersMap[CatalogManager.CommonCatalogsKey].Select(catalogContainer
                => catalogContainer.GetCatalogInfoAsync(CancellationToken.None)))).ToArray();

            var userCatalogInfos = (await Task.WhenAll(state.CatalogContainersMap[username].Select(catalogContainer
                => catalogContainer.GetCatalogInfoAsync(CancellationToken.None)))).ToArray();

            // Assert

            // common catalogs
            var actualCommonCatalogs = commonCatalogInfos
                .Select(catalogInfo => catalogInfo.Catalog)
                .ToList();

            Assert.Equal(2, actualCommonCatalogs.Count);

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
            var actualUserCatalogs = userCatalogInfos
                .Select(catalogInfo => catalogInfo.Catalog)
                .ToList();

            Assert.Equal(2, actualUserCatalogs.Count);

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
    }
}
