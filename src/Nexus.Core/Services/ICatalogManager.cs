using Nexus.Core;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    public interface ICatalogManager
    {
        CatalogState State { get; }

        Task UpdateAsync(CancellationToken cancellationToken);
    }
}