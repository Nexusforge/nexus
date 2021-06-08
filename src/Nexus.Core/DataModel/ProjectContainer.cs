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
            this.Catalog = new Catalog(id);
        }

        private CatalogContainer()
        {
            //
        }

        #endregion

        #region "Properties"

        public string Id { get; set; }

        public string PhysicalName => this.Id.TrimStart('/').Replace('/', '_');

        public DateTime CatalogBegin { get; set; }

        public DateTime CatalogEnd { get; set; }

        public Catalog Catalog { get; set; }

        public CatalogMeta CatalogMeta { get; set; }

        #endregion

        #region Methods

        public SparseCatalog ToSparseCatalog(List<Dataset> datasets)
        {
            var catalog = new SparseCatalog(this.Id, this.CatalogMeta.License);
            var channels = datasets.Select(dataset => dataset.Channel).Distinct().ToList();

            catalog.Channels = channels.Select(reference =>
            {
                var channelMeta = this.CatalogMeta.Channels.First(channelMeta => channelMeta.Id == reference.Id);

                var channel = new Channel(reference.Id, catalog)
                {
                    Name = reference.Name,
                    Group = reference.Group,

                    Unit = !string.IsNullOrWhiteSpace(channelMeta.Unit) 
                        ? channelMeta.Unit
                        : reference.Unit,

                    Description = !string.IsNullOrWhiteSpace(channelMeta.Description)
                        ? channelMeta.Description
                        : reference.Description
                };

                var referenceDatasets = datasets.Where(dataset => (Channel)dataset.Channel == reference);

                channel.Datasets = referenceDatasets.Select(referenceDataset =>
                {
                    return new Dataset(referenceDataset.Id, channel)
                    {
                        DataType = referenceDataset.DataType,
                        Registration = referenceDataset.Registration
                    };
                }).ToList();

                return channel;
            }).ToList();

            return catalog;
        }

        #endregion
    }
}