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

        public Catalog ToSparseCatalog(List<DatasetRecord> datasetRecords)
        {
            var resources = new List<Resource>();

            foreach (var datasetRecord in datasetRecords)
            {
                var resource = resources.FirstOrDefault(resource => resource.Id == datasetRecord.Resource.Id);

                if (resource is null)
                    resources.Add(datasetRecord.Resource with { Datasets = new List<Dataset>() });

                resource.Datasets.Add(datasetRecord.Dataset with { });
            }

            return this.Catalog with { Resources = resources };
        }

        #endregion
    }
}