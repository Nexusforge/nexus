using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.Sources;
using Nexus.Utilities;
using System.Security.Claims;
using Xunit;

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
                                ("A", "/") => Task.FromResult(new CatalogRegistration[] { new CatalogRegistration("/A"), new CatalogRegistration("/B/A") }),
                                ("A", "/A/") => Task.FromResult(new CatalogRegistration[] { new CatalogRegistration("/A/B"), new CatalogRegistration("/A/B/C"), new CatalogRegistration("/A/C/A") }),
                                ("B", "/") => Task.FromResult(new CatalogRegistration[] { new CatalogRegistration("/A"), new CatalogRegistration("/B/B"), new CatalogRegistration("/B/B2") }),
                                ("C", "/") => Task.FromResult(new CatalogRegistration[] { new CatalogRegistration("/C/A") }),
                                ("Nexus.Sources." + nameof(Sample), "/") => Task.FromResult(new CatalogRegistration[0]),
                                _ => throw new Exception("Unsupported combination.")
                            };
                        });

                    return Task.FromResult(dataSourceController);
                });

            /* appState */
            var registrationA = new DataSourceRegistration(Type: "A", new Uri("", UriKind.Relative), new Dictionary<string, string>(), Publish: true);
            var registrationB = new DataSourceRegistration(Type: "B", new Uri("", UriKind.Relative), new Dictionary<string, string>(), Publish: true);
            var registrationC = new DataSourceRegistration(Type: "C", new Uri("", UriKind.Relative), new Dictionary<string, string>(), Publish: false);

            var appState = new AppState()
            {
                Project = new NexusProject(default!, new Dictionary<string, UserConfiguration>()
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

            /* userManagerWrapper */
            var userManagerWrapper = Mock.Of<IUserManagerWrapper>();

            /* => user A */
            var usernameA = "UserA";

            var claimsIdentityA = new ClaimsIdentity(
               new Claim[] {
                    new Claim(ClaimTypes.Name, usernameA),
                    new Claim(NexusClaims.IS_ADMIN, "true")
               },
               "Fake authentication type");

            var userA = new ClaimsPrincipal(claimsIdentityA);

            /* => user B */
            var usernameB = "UserB";

            var claimsIdentityB = new ClaimsIdentity(
               new Claim[] {
                    new Claim(ClaimTypes.Name, usernameB),
               },
               "Fake authentication type");

            var userB = new ClaimsPrincipal(claimsIdentityB);

            Mock.Get(userManagerWrapper)
               .Setup(userManagerWrapper => userManagerWrapper.GetClaimsPrincipalAsync(It.IsAny<string>()))
               .Returns<string>(username =>
               {
                   return username switch
                   {
                       "UserA"  => Task.FromResult((ClaimsPrincipal?)userA),
                       "UserB"  => Task.FromResult((ClaimsPrincipal?)userB),
                       _        => Task.FromResult<ClaimsPrincipal?>(default)
                   };
               });

            /* security options */
            var securityOptions = Options.Create(new SecurityOptions());

            /* catalogManager */
            var catalogManager = new CatalogManager(
                appState,
                dataControllerService,
                databaseService,
                userManagerWrapper,
                securityOptions,
                NullLogger<CatalogManager>.Instance);

            // act
            var root = CatalogContainer.CreateRoot(catalogManager, default!);
            var rootCatalogContainers = (await root.GetChildCatalogContainersAsync(CancellationToken.None)).ToArray();
            var ACatalogContainers = (await rootCatalogContainers[0].GetChildCatalogContainersAsync(CancellationToken.None)).ToArray();

            // assert '/'
            Assert.Equal(5, rootCatalogContainers.Length);

            Assert.Contains(
                rootCatalogContainers,
                container => container.Id == "/A" && container.DataSourceRegistration == registrationA && container.Owner == userA);

            Assert.Contains(
                rootCatalogContainers,
                container => container.Id == "/B/A" && container.DataSourceRegistration == registrationA && container.Owner == userA);

            Assert.Contains(
                rootCatalogContainers,
                container => container.Id == "/B/B" && container.DataSourceRegistration == registrationB && container.Owner == userB);

            Assert.Contains(
                rootCatalogContainers,
                container => container.Id == "/B/B2" && container.DataSourceRegistration == registrationB && container.Owner == userB);

            Assert.Contains(
                rootCatalogContainers,
                container => container.Id == "/C/A" && container.DataSourceRegistration == registrationC && container.Owner == userB);

            // assert 'A'
            Assert.Equal(2, ACatalogContainers.Length);

            Assert.Contains(
                ACatalogContainers,
                container => container.Id == "/A/B" && container.DataSourceRegistration == registrationA && container.Owner == userA);

            Assert.Contains(
                ACatalogContainers,
                container => container.Id == "/A/C/A" && container.DataSourceRegistration == registrationA && container.Owner == userA);
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
                default,
                Overrides: new ResourceCatalogBuilder(id: "/A")
                    .WithDescription("v2")
                    .Build());

            /* backend sources */
            var registration = new DataSourceRegistration(
                Type: "A", 
                ResourceLocator: new Uri("A", UriKind.Relative),
                Configuration: default!,
                Publish: true);

            /* catalog container */
            var catalogContainer = new CatalogContainer(
                new CatalogRegistration("/A"),
                default!, 
                registration,
                catalogMetadata, 
                default!,
                default!, 
                dataControllerService);

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
