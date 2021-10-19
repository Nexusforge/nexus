using Nexus.DataModel;
using Nexus.Extensibility;
using System.Collections.Generic;

namespace Nexus.Core
{
    public record CatalogState
    {
        public BackendSource AggregationBackendSource { get; init; }
        public CatalogCollection CatalogCollection { get; init; }
        public Dictionary<BackendSource, ResourceCatalog[]> BackendSourceToCatalogsMap { get; init; }
    }
}
