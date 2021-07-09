using System.Collections.Generic;

namespace Nexus.DataModel
{
    public interface INexusDatabase
    {
        List<CatalogContainer> CatalogContainers { get; }

        bool TryFind(string catalogId, string resourceIdOrName, string datasetId, out DatasetRecord datasetRecord, bool includeName = false);

        DatasetRecord Find(string catalogId, string resourceIdOrName, string datasetId, bool includeName = false);
    }
}