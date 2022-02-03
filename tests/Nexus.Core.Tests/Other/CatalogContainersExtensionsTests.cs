using Moq;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Other
{
    public class CatalogContainersExtensionsTests
    {
        [Fact]
        public async Task CanTryFindCatalogContainer()
        {
            // arrange
            var catalogManager = Mock.Of<ICatalogManager>();

            Mock.Get(catalogManager)
               .Setup(catalogManager => catalogManager.GetCatalogContainersAsync(
                   It.IsAny<CatalogContainer>(),
                   It.IsAny<CancellationToken>()))
               .Returns<CatalogContainer, CancellationToken>((container, token) =>
               {
                   return Task.FromResult(container.Id switch
                   {
                       "/" => new CatalogContainer[]
                       {
                           new CatalogContainer(new CatalogRegistration("/A"), default, default, default, catalogManager, default, default),
                       },
                       "/A" => new CatalogContainer[] 
                       { 
                           new CatalogContainer(new CatalogRegistration("/A/C"), default, default, default, catalogManager, default, default),
                           new CatalogContainer(new CatalogRegistration("/A/B"), default, default, default, catalogManager, default, default),
                           new CatalogContainer(new CatalogRegistration("/A/D"), default, default, default, catalogManager, default, default)
                       },
                       "/A/B" => new CatalogContainer[]
                       {
                           new CatalogContainer(new CatalogRegistration("/A/B/D"), default, default, default, catalogManager, default, default),
                           new CatalogContainer(new CatalogRegistration("/A/B/C"), default, default, default, catalogManager, default, default)
                       },
                       "/A/D" => new CatalogContainer[]
                       {
                           new CatalogContainer(new CatalogRegistration("/A/D/F"), default, default, default, catalogManager, default, default),
                           new CatalogContainer(new CatalogRegistration("/A/D/E"), default, default, default, catalogManager, default, default)
                       },
                       "/A/F" => new CatalogContainer[]
                       {
                           new CatalogContainer(new CatalogRegistration("/A/F/H"), default, default, default, catalogManager, default, default)
                       },
                       _ => throw new Exception("Unsupported combination.")
                   });
               });

            var root = CatalogContainer.CreateRoot(catalogManager, default);

            // act
            var catalogContainerA = await root.TryFindCatalogContainerAsync("/A/B/C", CancellationToken.None);
            var catalogContainerB = await root.TryFindCatalogContainerAsync("/A/D/E", CancellationToken.None);
            var catalogContainerC = await root.TryFindCatalogContainerAsync("/A/F/G", CancellationToken.None);

            // assert
            Assert.NotNull(catalogContainerA);
            Assert.Equal("/A/B/C", catalogContainerA.Id);

            Assert.NotNull(catalogContainerB);
            Assert.Equal("/A/D/E", catalogContainerB.Id);

            Assert.Null(catalogContainerC);
        }
    }
}