﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Sources;
using Nexus.Utilities;
using System.Collections.ObjectModel;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Services
{
    public class CatalogManagerTests
    {
        delegate bool GobbleReturns(string catalogId, out string catalogMetadata);

        [Fact]
        public async Task CanCreateCatalogHierarchy()
        {
            // Test case:
            // User A, admin,
            //      /   => /A, /B/A
            //      /A/ => /A/B, /A/B/C (should be ignored), /A/C/A
            //
            // User B, no admin,
            //      /  => /A (should be ignored), /B/B, /B/B2, /C/A

            /* dataControllerService */
            var dataControllerService = Mock.Of<IDataControllerService>();

            Mock.Get(dataControllerService)
                .Setup(s => s.GetDataSourceControllerAsync(It.IsAny<DataSourceRegistration>(), It.IsAny<CancellationToken>()))
                .Returns<DataSourceRegistration, CancellationToken>((registration, cancellationToken) =>
                {
                    var dataSourceController = Mock.Of<IDataSourceController>();

                    Mock.Get(dataSourceController)
                        .Setup(s => s.GetCatalogRegistrationsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .Returns<string, CancellationToken>((path, cancellationToken) =>
                        {
                            var type = registration.Type;

                            return (type, path) switch
                            {
                                ("A", "/") => Task.FromResult(new CatalogRegistration[] { new CatalogRegistration("/A", string.Empty), new CatalogRegistration("/B/A", string.Empty) }),
                                ("A", "/A/") => Task.FromResult(new CatalogRegistration[] { new CatalogRegistration("/A/B", string.Empty), new CatalogRegistration("/A/B/C", string.Empty), new CatalogRegistration("/A/C/A", string.Empty) }),
                                ("B", "/") => Task.FromResult(new CatalogRegistration[] { new CatalogRegistration("/A", string.Empty), new CatalogRegistration("/B/B", string.Empty), new CatalogRegistration("/B/B2", string.Empty) }),
                                ("C", "/") => Task.FromResult(new CatalogRegistration[] { new CatalogRegistration("/C/A", string.Empty) }),
                                ("Nexus.Sources." + nameof(Sample), "/") => Task.FromResult(new CatalogRegistration[0]),
                                _ => throw new Exception("Unsupported combination.")
                            };
                        });

                    return Task.FromResult(dataSourceController);
                });

            /* appState */
            var registrationA = new DataSourceRegistration(Id: Guid.NewGuid(), Type: "A", new Uri("", UriKind.Relative), new Dictionary<string, string>());
            var registrationB = new DataSourceRegistration(Id: Guid.NewGuid(), Type: "B", new Uri("", UriKind.Relative), new Dictionary<string, string>());
            var registrationC = new DataSourceRegistration(Id: Guid.NewGuid(), Type: "C", new Uri("", UriKind.Relative), new Dictionary<string, string>());

            var appState = new AppState()
            {
                Project = new NexusProject(default!, default!, new Dictionary<string, UserConfiguration>()
                {
                    ["UserA"] = new UserConfiguration(new Dictionary<Guid, DataSourceRegistration>() 
                    { 
                        [Guid.NewGuid()] = registrationA
                    }),
                    ["UserB"] = new UserConfiguration(new Dictionary<Guid, DataSourceRegistration>() 
                    {
                        [Guid.NewGuid()] = registrationB,
                        [Guid.NewGuid()] = registrationC
                    })
                })
            };

            // databaseService
            var databaseService = Mock.Of<IDatabaseService>();

            Mock.Get(databaseService)
               .Setup(databaseService => databaseService.TryReadCatalogMetadata(
                   It.IsAny<string>(),
                   out It.Ref<string?>.IsAny))
               .Returns(new GobbleReturns((string catalogId, out string catalogMetadataString) =>
               {
                   catalogMetadataString = "{}";
                   return true;
               }));

            /* serviceProvider / dbService */
            var dbService = Mock.Of<IDBService>();
            var scope = Mock.Of<IServiceScope>();
            var scopeFactory = Mock.Of<IServiceScopeFactory>();
            var serviceProvider = Mock.Of<IServiceProvider>();

            Mock.Get(scope)
              .SetupGet(scope => scope.ServiceProvider)
              .Returns(serviceProvider);

            Mock.Get(scopeFactory)
              .Setup(scopeFactory => scopeFactory.CreateScope())
              .Returns(scope);

            Mock.Get(serviceProvider)
              .Setup(serviceProvider => serviceProvider.GetService(
                  It.Is<Type>(value => value == typeof(IDBService))))
              .Returns(dbService);

            Mock.Get(serviceProvider)
              .Setup(serviceProvider => serviceProvider.GetService(
                  It.Is<Type>(value => value == typeof(IServiceScopeFactory))))
              .Returns(scopeFactory);

            /* => user A */
            var usernameA = "UserA";

            var userA = new NexusUser()
            {
                Name = usernameA,
                Claims = new ReadOnlyDictionary<Guid, NexusClaim>(new Dictionary<Guid, NexusClaim>
                {
                    [Guid.NewGuid()] = new NexusClaim(Claims.Name, usernameA),
                    [Guid.NewGuid()] = new NexusClaim(Claims.Role, NexusRoles.ADMINISTRATOR)
                })
            };

            /* => user B */
            var usernameB = "UserB";

            var userB = new NexusUser()
            {
                Name = usernameB,
                Claims = new ReadOnlyDictionary<Guid, NexusClaim>(new Dictionary<Guid, NexusClaim>
                {
                    [Guid.NewGuid()] = new NexusClaim(Claims.Name, usernameB),
                })
            };

            Mock.Get(dbService)
               .Setup(dbService => dbService.FindUserAsync(It.IsAny<string>()))
               .Returns<string>(userId =>
               {
                   var result = userId switch
                   {
                       "UserA"  => Task.FromResult<NexusUser?>(userA),
                       "UserB"  => Task.FromResult<NexusUser?>(userB),
                       _        => Task.FromResult<NexusUser?>(default)
                   };

                   return result;
               });

            /* extensionHive */
            var extensionHive = Mock.Of<IExtensionHive>();

            /* catalogManager */
            var catalogManager = new CatalogManager(
                appState,
                dataControllerService,
                databaseService,
                serviceProvider,
                extensionHive,
                NullLogger<CatalogManager>.Instance);

            // act
            var root = CatalogContainer.CreateRoot(catalogManager, default!);
            var rootCatalogContainers = (await root.GetChildCatalogContainersAsync(CancellationToken.None)).ToArray();
            var ACatalogContainers = (await rootCatalogContainers[0].GetChildCatalogContainersAsync(CancellationToken.None)).ToArray();

            // assert '/'
            Assert.Equal(5, rootCatalogContainers.Length);

            Assert.Contains(
                rootCatalogContainers,
                container => container.Id == "/A" && container.DataSourceRegistration == registrationA && container.Owner!.Identity!.Name! == userA.Name);

            Assert.Contains(
                rootCatalogContainers,
                container => container.Id == "/B/A" && container.DataSourceRegistration == registrationA && container.Owner!.Identity!.Name! == userA.Name);

            Assert.Contains(
                rootCatalogContainers,
                container => container.Id == "/B/B" && container.DataSourceRegistration == registrationB && container.Owner!.Identity!.Name! == userB.Name);

            Assert.Contains(
                rootCatalogContainers,
                container => container.Id == "/B/B2" && container.DataSourceRegistration == registrationB && container.Owner!.Identity!.Name! == userB.Name);

            Assert.Contains(
                rootCatalogContainers,
                container => container.Id == "/C/A" && container.DataSourceRegistration == registrationC && container.Owner!.Identity!.Name! == userB.Name);

            // assert 'A'
            Assert.Equal(2, ACatalogContainers.Length);

            Assert.Contains(
                ACatalogContainers,
                container => container.Id == "/A/B" && container.DataSourceRegistration == registrationA && container.Owner!.Identity!.Name! == userA.Name);

            Assert.Contains(
                ACatalogContainers,
                container => container.Id == "/A/C/A" && container.DataSourceRegistration == registrationA && container.Owner!.Identity!.Name! == userA.Name);
        }

        [Fact]
        public async Task CanLoadLazyCatalogInfos()
        {
            // Arrange

            /* expected catalogs */
            var expectedCatalog = new ResourceCatalogBuilder(id: "/A")
                .AddResource(new ResourceBuilder(id: "A").AddRepresentation(new Representation(NexusDataType.INT16, TimeSpan.FromSeconds(1))).Build())
                .WithDescription("v2")
                .Build();

            /* expected time range response */
            var expectedTimeRange = new CatalogTimeRange(new DateTime(2020, 01, 01), new DateTime(2020, 01, 02));

            /* data controller service */
            var dataControllerService = Mock.Of<IDataControllerService>();

            Mock.Get(dataControllerService)
                .Setup(s => s.GetDataSourceControllerAsync(It.IsAny<DataSourceRegistration>(), It.IsAny<CancellationToken>()))
                .Returns<DataSourceRegistration, CancellationToken>((registration, cancellationToken) =>
                {
                    var dataSourceController = Mock.Of<IDataSourceController>();

                    Mock.Get(dataSourceController)
                        .Setup(s => s.GetCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(expectedCatalog);

                    Mock.Get(dataSourceController)
                        .Setup(s => s.GetTimeRangeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(expectedTimeRange);

                    return Task.FromResult(dataSourceController);
                });

            /* catalog metadata */
            var catalogMetadata = new CatalogMetadata(
                default, 
                default,
                Overrides: new ResourceCatalogBuilder(id: "/A")
                    .WithDescription("v2")
                    .Build());

            /* backend sources */
            var registration = new DataSourceRegistration(
                Id: Guid.NewGuid(),
                Type: "A", 
                ResourceLocator: new Uri("A", UriKind.Relative),
                Configuration: default!);

            /* catalog container */
            var catalogContainer = new CatalogContainer(
                new CatalogRegistration("/A", string.Empty),
                default!, 
                registration,
                default!,
                catalogMetadata,
                default!,
                default!,
                dataControllerService);

            // Act
            var lazyCatalogInfo = await catalogContainer.GetLazyCatalogInfoAsync(CancellationToken.None);

            // Assert
            var actualJsonString = JsonSerializerHelper.SerializeIntended(lazyCatalogInfo.Catalog);
            var expectedJsonString = JsonSerializerHelper.SerializeIntended(expectedCatalog);

            Assert.Equal(actualJsonString, expectedJsonString);
            Assert.Equal(new DateTime(2020, 01, 01), lazyCatalogInfo.Begin);
            Assert.Equal(new DateTime(2020, 01, 02), lazyCatalogInfo.End);
        }
    }
}
