using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;

namespace Nexus.Services
{
    internal record CatalogManagerState
    {
        public BackendSource AggregationBackendSource { get; init; }
        public CatalogCollection Catalogs { get; init; }
        public Dictionary<BackendSource, Type> BackendSourceToDataReaderTypeMap { get; init; }
        public Dictionary<BackendSource, ResourceCatalog[]> BackendSourceToCatalogsMap { get; init; }
    }
}
