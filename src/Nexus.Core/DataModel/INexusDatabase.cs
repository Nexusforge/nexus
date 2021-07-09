using System.Collections.Generic;

namespace Nexus.DataModel
{
    public interface INexusDatabase
    {
        List<CatalogContainer> CatalogContainers { get; }

        bool TryFind(string catalogId, string resourceIdOrName, string representationId, out CatalogItem catalogItem);

        CatalogItem Find(string catalogId, string resourceIdOrName, string representationId);
    }
}