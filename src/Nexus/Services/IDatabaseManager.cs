using Nexus.DataModel;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    public interface IDatabaseManager
    {
        NexusDatabase Database { get; }
        DatabaseManager.DatabaseManagerState State { get; }
        void SaveConfig(string folderPath, NexusDatabaseConfig config);
        void SaveCatalogMeta(CatalogProperties catalogMeta);
        Task UpdateAsync(CancellationToken cancellationToken);
    }
}