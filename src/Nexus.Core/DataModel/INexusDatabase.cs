using System.Collections.Generic;

namespace Nexus.DataModel
{
    public interface INexusDatabase
    {
        List<CatalogContainer> CatalogContainers { get; }

        bool TryFind(string catalogId, string resourceIdOrName, string representationId, out RepresentationRecord representationRecord, bool includeName = false);

        RepresentationRecord Find(string catalogId, string resourceIdOrName, string representationId, bool includeName = false);
    }
}