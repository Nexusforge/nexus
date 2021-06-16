using System.Collections.Generic;

namespace Nexus.DataModel
{
    public interface INexusDatabase
    {
        List<CatalogContainer> CatalogContainers { get; }

        bool TryFindDatasetById(string catalogId, string channelIdOrName, string datsetId, out Dataset dataset);
    }
}