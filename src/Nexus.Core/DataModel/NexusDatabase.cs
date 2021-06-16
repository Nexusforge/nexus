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

        public List<Catalog> GetCatalogs()
        {
            return this.CatalogContainers.Select(container => container.Catalog).ToList();
        }

        public bool TryFindCatalogById(string id, out Catalog catalog)
        {
            var catalogContainer = this.CatalogContainers.FirstOrDefault(catalogContainer => catalogContainer.Id == id);
            catalog = catalogContainer?.Catalog;

            return catalog != null;
        }

        public bool TryFindChannelByIdOrName(string catalogId, string channelIdOrName, out Channel channel)
        {
            channel = default;

            if (this.TryFindCatalogById(catalogId, out var catalog))
            {
                if (Guid.TryParse(channelIdOrName, out var channelId))
                    channel = catalog.Channels.FirstOrDefault(current => current.Id == channelId);

                if (channel == null)
                    channel = catalog.Channels.FirstOrDefault(current => current.Name == channelIdOrName);
            }

            return channel != null;
        }

        public bool TryFindDatasetById(string catalogId, string channelIdOrName, string datsetId, out Dataset dataset)
        {
            dataset = default;

            if (this.TryFindChannelByIdOrName(catalogId, channelIdOrName, out var channel))
            {
                dataset = channel.Datasets.FirstOrDefault(dataset => dataset.Id == datsetId);

                if (dataset != null)
                    return true;
            }

            return false;
        }

        public bool TryFindDataset(string path, out Dataset dataset)
        {
            var pathSegments = path.Split("/");

            if (pathSegments.Length != 6)
                throw new Exception($"The channel path '{path}' is invalid.");

            var catalogName = $"/{pathSegments[1]}/{pathSegments[2]}/{pathSegments[3]}";
            var channelName = pathSegments[4];
            var datasetName = pathSegments[5];

            return this.TryFindDatasetById(catalogName, channelName, datasetName, out dataset);
        }

        public bool TryFindDatasetsByGroup(string groupPath, out List<Dataset> datasets)
        {
            var groupPathSegments = groupPath.Split("/");

            if (groupPathSegments.Length != 6)
                throw new Exception($"The group path '{groupPath}' is invalid.");

            var catalogName = $"/{groupPathSegments[1]}/{groupPathSegments[2]}/{groupPathSegments[3]}";
            var groupName = groupPathSegments[4];
            var datasetName = groupPathSegments[5];

            return this.TryFindDatasetsByGroup(catalogName, groupName, datasetName, out datasets);
        }

        private bool TryFindDatasetsByGroup(string catalogName, string groupName, string datasetName, out List<Dataset> datasets)
        {
            datasets = new List<Dataset>();

            var catalogContainer = this.CatalogContainers.FirstOrDefault(catalogContainer => catalogContainer.Id == catalogName);

            if (catalogContainer != null)
            {
                var channels = catalogContainer.Catalog.Channels
                    .Where(channel => channel.Group.Split('\n')
                    .Contains(groupName))
                    .OrderBy(channel => channel.Name)
                    .ToList();

                datasets
                    .AddRange(channels
                    .SelectMany(channel => channel.Datasets
                    .Where(dataset => dataset.Id == datasetName)));

                if (datasets.Any())
                    return true;
            }

            return false;
        }

        #endregion
    }
}