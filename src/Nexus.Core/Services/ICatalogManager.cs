using Nexus.Core;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    public interface ICatalogManager
    {
        Task<CatalogState> LoadCatalogsAsync(CancellationToken cancellationToken);
    }
}