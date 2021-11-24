using Nexus.Core;
using Nexus.DataModel;
using System;
using System.Collections.Generic;

namespace Nexus.Services
{
    internal record CatalogState(
        Dictionary<string, List<CatalogContainer>> CatalogContainersMap,
        CatalogCache CatalogCache);

    internal record CatalogInfo(DateTime Begin, DateTime End, ResourceCatalog Catalog);
}
