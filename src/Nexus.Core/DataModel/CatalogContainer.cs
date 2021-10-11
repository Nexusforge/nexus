using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Id,nq}")]
    public class CatalogContainer
    {
        #region "Constructors"

        public CatalogContainer(string id)
        {
            this.Id = id;
            this.Catalog = new ResourceCatalog(id);
        }

        #endregion

        #region "Properties"

        public string Id { get; set; }

        public string PhysicalName => this.Id.TrimStart('/').Replace('/', '_');

        public DateTime CatalogBegin { get; set; }

        public DateTime CatalogEnd { get; set; }

        public ResourceCatalog Catalog { get; set; }

        public ResourceCatalog CatalogMeta { get; set; }

        public CatalogProperties CatalogProperties { get; set; }

        #endregion
    }
}