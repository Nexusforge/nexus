using Nexus.DataModel;
using Nexus.Extensibility;
using System.Collections.Generic;

namespace Nexus.Core
{
    internal record CatalogState(BackendSource AggregationBackendSource, CatalogCollection CatalogCollection, Dictionary<BackendSource, ResourceCatalog[]> BackendSourceToCatalogsMap);
}
