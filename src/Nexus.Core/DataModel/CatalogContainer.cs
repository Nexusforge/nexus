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
            this.Catalog = new Catalog() { Id = id };
        }

        #endregion

        #region "Properties"

        public string Id { get; set; }

        public string PhysicalName => this.Id.TrimStart('/').Replace('/', '_');

        public DateTime CatalogBegin { get; set; }

        public DateTime CatalogEnd { get; set; }

        public Catalog Catalog { get; set; }

        public Catalog CatalogMeta { get; set; }

        public CatalogProperties CatalogProperties { get; set; }

        #endregion

        #region Methods

        public Catalog ToSparseCatalog(List<RepresentationRecord> representationRecords)
        {
            var resources = new List<Resource>();

            foreach (var representationRecord in representationRecords)
            {
                var resource = resources.FirstOrDefault(resource => resource.Id == representationRecord.Resource.Id);

                if (resource is null)
                    resources.Add(representationRecord.Resource with { Representations = new List<Representation>() });

                resource.Representations.Add(representationRecord.Representation with { });
            }

            return this.Catalog with { Resources = resources };
        }

        #endregion
    }
}