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

        public bool TryFind(string catalogId, string resourceIdOrName, string representationId, out CatalogItem catalogItem, bool includeName = false)
        {
            var resourcePath = $"{catalogId}/{resourceIdOrName}/{representationId}";
            return this.TryFind(resourcePath, out catalogItem, includeName);
        }


        public bool TryFind(string resourcePath, out CatalogItem catalogItem, bool includeName = false)
        {
            catalogItem = default(CatalogItem);

            foreach (var container in this.CatalogContainers)
            {
                if (container.Catalog.TryFind(resourcePath, out catalogItem, includeName))
                    break;
            }

            if (catalogItem is null)
                return false;

            return true;
        }

        public CatalogItem Find(string catalogId, string resourceIdOrName, string representationId, bool includeName = false)
        {
            this.TryFind(catalogId, resourceIdOrName, representationId, out var catalogItem, includeName);

            if (catalogItem is null)
                throw new Exception($"The representation on path '{catalogId}/{resourceIdOrName}/{representationId}' could not be found.");

            return catalogItem;
        }

        #endregion
    }
}