using Nexus.Core;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal interface ICatalogManager
    {
        Task<CatalogContainer[]> GetCatalogContainersAsync(
            CatalogContainer parent,
            CancellationToken cancellationToken);
    }
}