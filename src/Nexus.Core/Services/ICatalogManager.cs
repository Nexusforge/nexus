using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal interface ICatalogManager
    {
        Task<CatalogState> CreateCatalogStateAsync(
            CancellationToken cancellationToken);

        Task AttachChildCatalogIdsAsync(
            CatalogContainer parent,
            CatalogContainersMap catalogContainersMap,
            CancellationToken cancellationToken);

        Task<CatalogInfo> LoadCatalogInfoAsync(
            string catalogId,
            BackendSource backendSource,
            ResourceCatalog? catalogOverrides,
            CancellationToken cancellationToken);
    }
}