﻿using Nexus.Core;
using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Nexus.Services
{
    internal record CatalogState(
        BackendSource AggregationBackendSource,
        CatalogContainer[] CatalogContainers,
        Dictionary<BackendSource, string[]> BackendSourceToCatalogIdsMap,
        ConcurrentDictionary<BackendSource, ConcurrentDictionary<string, ResourceCatalog>> BackendSourceCache);

    internal record CatalogInfo(DateTime Begin, DateTime End, ResourceCatalog Catalog);
}