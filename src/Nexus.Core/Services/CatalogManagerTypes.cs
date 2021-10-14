using Nexus.DataModel;
using Nexus.Extensibility;
using System.Collections.Generic;

namespace Nexus.Services
{
    internal record CatalogManagerState
    {
        public BackendSource AggregationBackendSource { get; init; }
        public CatalogContainerCollection Catalogs { get; init; }
        public Dictionary<BackendSource, ResourceCatalog[]> BackendSourceToCatalogsMap { get; init; }
    }
}
