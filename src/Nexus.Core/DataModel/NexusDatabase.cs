using System;
using System.Collections.Generic;

namespace Nexus.DataModel
{
    public class NexusDatabase : INexusDatabase
    {
        #region Constructors

        public NexusDatabase()
        {
            this.CatalogContainers = new List<CatalogContainer>();
        }

        #endregion

        #region Properties

        public List<CatalogContainer> CatalogContainers { get; set; }

        #endregion

        #region Methods

        public bool TryFind(string catalogId, string resourceIdOrName, string representationId, out RepresentationRecord representationRecord, bool includeName = false)
        {
            var resourcePath = $"{catalogId}/{resourceIdOrName}/{representationId}";
            return this.TryFind(resourcePath, out representationRecord, includeName);
        }


        public bool TryFind(string resourcePath, out RepresentationRecord representationRecord, bool includeName = false)
        {
            representationRecord = default(RepresentationRecord);

            foreach (var container in this.CatalogContainers)
            {
                if (container.Catalog.TryFind(resourcePath, out representationRecord, includeName))
                    break;
            }

            if (representationRecord is null)
                return false;

            return true;
        }

        public RepresentationRecord Find(string catalogId, string resourceIdOrName, string representationId, bool includeName = false)
        {
            this.TryFind(catalogId, resourceIdOrName, representationId, out var representationRecord, includeName);

            if (representationRecord is null)
                throw new Exception($"The representation on path '{catalogId}/{resourceIdOrName}/{representationId}' could not be found.");

            return representationRecord;
        }

        #endregion
    }
}