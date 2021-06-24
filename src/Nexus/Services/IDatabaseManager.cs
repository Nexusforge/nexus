using Nexus.DataModel;
using Nexus.Extensibility;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Services
{
    public interface IDatabaseManager
    {
        NexusDatabase Database { get; }
        DatabaseManager.DatabaseManagerState State { get; }
        Task<DataSourceController> GetDataSourceControllerAsync(ClaimsPrincipal user, BackendSource backendSource, CancellationToken cancellationToken, DatabaseManager.DatabaseManagerState state = null);
        Task<List<DataSourceController>> GetDataSourcesAsync(ClaimsPrincipal user, string catalogId, CancellationToken cancellationToken);
        void SaveConfig(string folderPath, NexusDatabaseConfig config);
        void SaveCatalogMeta(CatalogMeta catalogMeta);
        Task UpdateAsync(CancellationToken cancellationToken);
    }
}