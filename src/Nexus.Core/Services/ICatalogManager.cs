using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal interface ICatalogManager
    {
        Task<CatalogState> LoadCatalogsAsync(CancellationToken cancellationToken);
    }
}