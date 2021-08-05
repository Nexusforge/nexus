using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;

namespace Nexus.Services
{
    internal record DatabaseManagerState
    {
        public BackendSource AggregationBackendSource { get; init; }
        public NexusDatabase Database { get; init; }
        public Dictionary<BackendSource, Type> BackendSourceToDataReaderTypeMap { get; init; }
        public Dictionary<BackendSource, ResourceCatalog[]> BackendSourceToCatalogsMap { get; init; }
    }
}
