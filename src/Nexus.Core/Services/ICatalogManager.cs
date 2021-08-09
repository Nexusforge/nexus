using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal interface ICatalogManager
    {
        CatalogManagerState State { get; }

        Task UpdateAsync(CancellationToken cancellationToken);
    }
}