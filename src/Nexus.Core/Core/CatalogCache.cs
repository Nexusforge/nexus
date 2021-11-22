using Nexus.DataModel;
using Nexus.Extensibility;
using System.Collections.Concurrent;

namespace Nexus.Core
{
    internal class CatalogCache : ConcurrentDictionary<BackendSource, ConcurrentDictionary<string, ResourceCatalog>>
    {
        // This cache is required for DataSourceController.ReadAsync method to store original catalog items.
    }
}
