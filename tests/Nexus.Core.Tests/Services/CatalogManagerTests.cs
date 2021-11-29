using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Models;
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
            // Catalog "/B2"    of User B should become part of the user catalog containers list
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
                                ("B", "/") => Task.FromResult(new[] { "/A", "/B", "/B2" }),
                                ("C", "/") => Task.FromResult(new[] { "/C/A" }),
                                _ => throw new Exception("Unsupported combination.")
                            };
                        });

                    return Task.FromResult(dataSourceController);
                });

            /* databaseManager */
            var backendSourceA = new BackendSource(Type: "A", new Uri("", UriKind.Relative), new Dictionary<string, string>(), Publish: true);
            var backendSourceB = new BackendSource(Type: "B", new Uri("", UriKind.Relative), new Dictionary<string, string>(), Publish: true);
            var backendSourceC = new BackendSource(Type: "C", new Uri("", UriKind.Relative), new Dictionary<string, string>(), Publish: false);

            var userAConfig = new UserConfiguration("UserA", new List<BackendSource>() { backendSourceA });
            var userBConfig = new UserConfiguration("UserB", new List<BackendSource>() { backendSourceB, backendSourceC });

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
            Assert.Equal(2, state.CatalogContainersMap["UserB"].Count);

            Assert.Contains(
                state.CatalogContainersMap[CatalogManager.CommonCatalogsKey],
                container => container.Id == "/A" && container.BackendSource == backendSourceA);

            Assert.Contains(
                state.CatalogContainersMap[CatalogManager.CommonCatalogsKey],
                container => container.Id == "/B" && container.BackendSource == backendSourceB);

            Assert.Contains(
                state.CatalogContainersMap["UserB"],
                container => container.Id == "/B2" && container.BackendSource == backendSourceB);

            Assert.Contains(
                state.CatalogContainersMap["UserB"],
                container => container.Id == "/C/A" && container.BackendSource == backendSourceC);
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

            /* backend source */
            var backendSource = new BackendSource(default, default, default, default);

            /* catalog containers map */
            var parent = new CatalogContainer("/A", user, backendSource, default, catalogManager);

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
            var expectedCatalog = new ResourceCatalogBuilder(id: "/A")
                .AddResource(new ResourceBuilder(id: "A").AddRepresentation(new Representation(NexusDataType.INT16, TimeSpan.FromSeconds(1))).Build())
                .WithDescription("v2")
                .Build();

            /* expected time range response */
            var expectedTimeRange = new TimeRangeResponse(new DateTime(2020, 01, 01), new DateTime(2020, 01, 02));

            /* data controller service */
            var dataControllerService = Mock.Of<IDataControllerService>();

            Mock.Get(dataControllerService)
                .Setup(s => s.GetDataSourceControllerAsync(It.IsAny<BackendSource>(), It.IsAny<CancellationToken>(), It.IsAny<CatalogCache>()))
                .Returns<BackendSource, CancellationToken, CatalogCache>((backendSource, cancellationToken, catalogCache) =>
                {
                    var dataSourceController = Mock.Of<IDataSourceController>();

                    Mock.Get(dataSourceController)
                        .Setup(s => s.GetCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .Returns<string, CancellationToken>((catalogId, cancellationToken) =>
                        {
                            return Task.FromResult(expectedCatalog);
                        });

                    Mock.Get(dataSourceController)
                        .Setup(s => s.GetTimeRangeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .Returns<string, CancellationToken>((catalogId, cancellationToken) =>
                        {
                            return Task.FromResult(expectedTimeRange);
                        });

                    return Task.FromResult(dataSourceController);
                });

            /* catalog metadata */
            var catalogMetadata = new CatalogMetadata()
            {
                Overrides = new ResourceCatalogBuilder(id: "/A")
                    .WithDescription("v2")
                    .Build()
            };

            /* backend sources */
            var backendSource = new BackendSource(
                Type: "A", 
                ResourceLocator: new Uri("A", UriKind.Relative),
                Configuration: default,
                Publish: true);

            /* catalog manager */
            var catalogManager = new CatalogManager(dataControllerService, default, default, Options.Create(new SecurityOptions()), default);

            /* catalog container */
            var catalogContainer = new CatalogContainer("/A", default, backendSource, catalogMetadata, catalogManager);

            // Act
            var catalogInfo = await catalogContainer.GetCatalogInfoAsync(CancellationToken.None);

            // Assert
            var actualJsonString = JsonSerializerHelper.SerializeIntended(catalogInfo.Catalog);
            var expectedJsonString = JsonSerializerHelper.SerializeIntended(expectedCatalog);

            Assert.Equal(actualJsonString, expectedJsonString);
            Assert.Equal(new DateTime(2020, 01, 01), catalogInfo.Begin);
            Assert.Equal(new DateTime(2020, 01, 02), catalogInfo.End);
        }
    }
}
