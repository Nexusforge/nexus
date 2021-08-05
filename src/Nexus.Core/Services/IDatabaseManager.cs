using Nexus.DataModel;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    internal interface IDatabaseManager
    {
        NexusDatabase Database { get; }
        DatabaseManagerState State { get; }
        void SaveConfig(string folderPath, NexusDatabaseConfig config);
        void SaveCatalogMeta(CatalogProperties catalogMeta);
        Task UpdateAsync(CancellationToken cancellationToken);
    }
}