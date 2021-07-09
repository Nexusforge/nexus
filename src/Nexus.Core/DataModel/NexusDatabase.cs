using System;
using System.Collections.Generic;
using System.Linq;

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
            return this.CatalogContainers
                .Select(container => container.Catalog)
                .TryFind(resourcePath, out catalogItem, includeName);
        }

        public CatalogItem Find(string catalogId, string resourceIdOrName, string representationId, bool includeName = false)
        {
            if (!this.TryFind(catalogId, resourceIdOrName, representationId, out var catalogItem, includeName))
                throw new Exception($"The resource path '{catalogId}/{resourceIdOrName}/{representationId}' could not be found.");

            return catalogItem;
        }

        #endregion
    }
}