using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.DataModel
{
    public class CatalogCollection
    {
        #region Constructors

        public CatalogCollection()
        {
            this.CatalogContainers = new List<CatalogContainer>();
        }

        public CatalogCollection(List<CatalogContainer> catalogContainers)
        {
            this.CatalogContainers = catalogContainers;
        }

        #endregion

        #region Properties

        public List<CatalogContainer> CatalogContainers { get; set; }

        #endregion

        #region Methods

        public bool TryFind(string catalogId, string resourceIdOrName, string representationId, out CatalogItem catalogItem)
        {
            var resourcePath = $"{catalogId}/{resourceIdOrName}/{representationId}";
            return this.TryFind(resourcePath, out catalogItem);
        }

        public bool TryFind(string resourcePath, out CatalogItem catalogItem)
        {
            return this.CatalogContainers
                .Select(container => container.Catalog)
                .TryFind(resourcePath, out catalogItem);
        }

        public CatalogItem Find(string catalogId, string resourceIdOrName, string representationId)
        {
            if (!this.TryFind(catalogId, resourceIdOrName, representationId, out var catalogItem))
                throw new Exception($"The resource path '{catalogId}/{resourceIdOrName}/{representationId}' could not be found.");

            return catalogItem;
        }

        #endregion
    }
}