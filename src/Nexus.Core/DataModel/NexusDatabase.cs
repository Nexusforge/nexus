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

        public bool TryFind(string catalogId, string channelIdOrName, string datasetId, out DatasetRecord datasetRecord, bool includeName = false)
        {
            var datasetPath = $"{catalogId}/{channelIdOrName}/{datasetId}";
            datasetRecord = default(DatasetRecord);

            foreach (var container in this.CatalogContainers)
            {
                if (container.Catalog.TryFind(datasetPath, out datasetRecord, includeName))
                    break;
            }

            if (datasetRecord is null)
                return false;

            return true;
        }

        public DatasetRecord Find(string catalogId, string channelIdOrName, string datasetId, bool includeName = false)
        {
            this.TryFind(catalogId, channelIdOrName, datasetId, out var datasetRecord, includeName);

            if (datasetRecord is null)
                throw new Exception($"The dataset on path '{catalogId}/{channelIdOrName}/{datasetId}' could not be found.");

            return datasetRecord;
        }

        #endregion
    }
}