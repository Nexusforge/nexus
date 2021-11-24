using Nexus.DataModel;
using Nexus.Extensibility;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal interface ICatalogManager
    {
        Task<CatalogState> CreateCatalogStateAsync(
            CancellationToken cancellationToken);

        Task<CatalogInfo> LoadCatalogInfoAsync(
            string catalogId,
            BackendSource backendSource,
            ResourceCatalog? catalogOverrides,
            CancellationToken cancellationToken);
        Task LoadCatalogIdsAsync(
            string relativeTo,
            BackendSource backendSource,
            ClaimsPrincipal user,
            CatalogState state,
            CancellationToken cancellationToken);
    }
}