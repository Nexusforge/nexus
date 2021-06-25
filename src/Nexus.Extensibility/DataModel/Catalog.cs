using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Id,nq}")]
    public record Catalog
    {
        #region Properties

        public string Id { get; init; }
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
        public List<Channel> Channels { get; init; } = new List<Channel>();

        #endregion

        #region "Methods"

        public Catalog Merge(Catalog catalog, MergeMode mergeMode)
        {
            if (this.Id != catalog.Id)
                throw new Exception("The catalog to be merged has a different ID.");

            // merge channels
            var mergedChannels = new List<Channel>();

            foreach (var channel in catalog.Channels)
            {
                var referenceChannel = mergedChannels.FirstOrDefault(current => current.Id == channel.Id);

                if (referenceChannel != null)
                {
                    mergedChannels.Add(referenceChannel.Merge(channel, mergeMode));
                }
                else
                {
                    mergedChannels.Add(channel with
                    {
                        Metadata = channel.Metadata.ToDictionary(entry => entry.Key, entry => entry.Value),
                        Datasets = channel.Datasets.ToList()
                    });
                }
            }

            // merge properties
            Catalog merged;

            switch (mergeMode)
            {
                case MergeMode.ExclusiveOr:

                    var mergedMetadata1 = this.Metadata
                        .ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var (key, value) in catalog.Metadata)
                    {
                        if (mergedMetadata1.ContainsKey(key))
                            throw new Exception($"The left catalog's metadata already contains the key '{key}'.");

                        else
                            mergedMetadata1[key] = value;
                    }

                    merged = new Catalog()
                    {
                        Id = this.Id,
                        Metadata = mergedMetadata1,
                        Channels = mergedChannels
                    };

                    break;

                case MergeMode.NewWins:

                    var mergedMetadata2 = this.Metadata
                        .ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var (key, value) in catalog.Metadata)
                    {
                        mergedMetadata2[key] = value;
                    }

                    merged = new Catalog()
                    {
                        Id = this.Id,
                        Metadata = mergedMetadata2,
                        Channels = mergedChannels
                    };

                    break;

                default:
                    throw new NotSupportedException();
            }

            return merged;
        }

        public bool TryFind(string datasetPath, out DatasetRecord datasetRecord, bool includeName = false)
        {
            datasetRecord = null;

            var pathParts = datasetPath.Split("/");
            var catalogId = string.Join('/', pathParts.Take(pathParts.Length - 2));
            var channelId = Guid.Parse(pathParts[4]);
            var datasetId = pathParts[5];

            if (catalogId != this.Id)
                return false;

            var channel = this.Channels.FirstOrDefault(channel => channel.Id == channelId);

            if (channel is null)
            {
                if (includeName)
                    channel = this.Channels.FirstOrDefault(channel => channel.Name == pathParts[4]);

                if (channel is null)
                    return false;
            }

            var dataset = channel.Datasets.FirstOrDefault(dataset => dataset.Id == datasetId);

            if (dataset is null)
                return false;

            datasetRecord = new DatasetRecord(this, channel, dataset);
            return true;
        }

        public DatasetRecord Find(string datasetPath, bool includeName = false)
        {
            if (!this.TryFind(datasetPath, out var datasetRecord, includeName))
                throw new Exception($"The dataset on path '{datasetPath}' could not be found.");

            return datasetRecord;
        }

        public static DatasetRecord Find(string datasetPath, IEnumerable<Catalog> catalogs, bool includeName = false)
        {
            var datasetRecord = default(DatasetRecord);

            foreach (var catalog in catalogs)
            {
                if (catalog.TryFind(datasetPath, out datasetRecord, includeName))
                    break;
            }

            if (datasetRecord is null)
                throw new Exception($"The dataset on path '{datasetPath}' could not be found.");

            return datasetRecord;
        }

        #endregion
    }
}
