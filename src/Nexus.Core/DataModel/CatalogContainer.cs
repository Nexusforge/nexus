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

        public CatalogSettings CatalogSettings { get; set; }

        #endregion

        #region Methods

        public Catalog ToSparseCatalog(List<Dataset> datasets)
        {
            var channels = new List<Channel>();

            foreach (var dataset in datasets)
            {
                var channel = channels.FirstOrDefault(channel => channel.Id == dataset.Channel.Id);

                if (channel is null)
                    channels.Add(dataset.Channel with { Datasets = new List<Dataset>() });

                channel.Datasets.Add(dataset);
            }

            return this.Catalog with { Channels = channels };
        }

        #endregion
    }
}