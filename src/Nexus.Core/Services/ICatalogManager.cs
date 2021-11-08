using Nexus.DataModel;
using Nexus.Extensibility;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal interface ICatalogManager
    {
        Task<CatalogState> LoadCatalogsAsync(
            CancellationToken cancellationToken);

        Task<CatalogInfo> LoadCatalogInfoAsync(
            string catalogId,
            BackendSource[] backendSources,
            ResourceCatalog? catalogOverrides,
            CancellationToken cancellationToken);
    }
}