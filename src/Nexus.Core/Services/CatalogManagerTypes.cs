using Nexus.Core;
using Nexus.DataModel;
using System;

namespace Nexus.Services
{
    internal record CatalogState(
        CatalogContainer Root,
        CatalogCache Cache);

    internal record CatalogInfo(DateTime Begin, DateTime End, ResourceCatalog Catalog);
}
