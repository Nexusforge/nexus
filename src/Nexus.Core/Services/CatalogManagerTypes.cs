using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;

namespace Nexus.Services
{
    internal record CatalogState(
        BackendSource AggregationBackendSource,
        CatalogContainer[] CatalogContainers,
        Dictionary<BackendSource, string[]> BackendSourceToCatalogIdsMap,
        BackendSourceCache BackendSourceCache);

    internal record CatalogInfo(DateTime Begin, DateTime End, ResourceCatalog Catalog);
}
