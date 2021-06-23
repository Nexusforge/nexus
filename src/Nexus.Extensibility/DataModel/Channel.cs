using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Serialization;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Name,nq}")]
    public record Channel
    {
        #region Properties

        public Guid Id { get; init; }
        public string Name { get; init; }
        public string Group { get; init; }
        public string Unit { get; init; }
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
        public List<Dataset> Datasets { get; init; } = new List<Dataset>();

        [JsonIgnore]
        public Catalog Catalog { get; internal set; }

        #endregion

        #region "Methods"

        internal Channel Merge(Channel channel, ChannelMergeMode mergeMode)
        {
            // merge datasets
            var mergedDatasets = this.Datasets
                .Select(dataset => dataset with { })
                .ToList();

            foreach (var dataset in channel.Datasets)
            {
                var referenceDataset = this.Datasets.FirstOrDefault(current => current.Id == dataset.Id);

                if (referenceDataset != null)
                    mergedDatasets.Add(referenceDataset.Merge(dataset));

                else
                    mergedDatasets.Add(dataset);
            }

            // merge properties
            Channel merged;

            switch (mergeMode)
            {
                case ChannelMergeMode.OverwriteMissing:

                    var mergedMetaData1 = this.Metadata
                        .ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var (key, value) in channel.Metadata)
                    {
                        if (!mergedMetaData1.ContainsKey(key))
                            mergedMetaData1[key] = value;
                    }

                    merged = new Channel()
                    {
                        Id = this.Id,
                        Name = string.IsNullOrWhiteSpace(this.Name) ? channel.Name : this.Name,
                        Group = string.IsNullOrWhiteSpace(this.Group) ? channel.Group : this.Group,
                        Unit = string.IsNullOrWhiteSpace(this.Unit) ? channel.Unit : this.Unit,
                        Metadata = mergedMetaData1,
                        Datasets = mergedDatasets
                    };

                    break;

                case ChannelMergeMode.NewWins:

                    var mergedMetaData2 = this.Metadata
                        .ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var (key, value) in channel.Metadata)
                    {
                        mergedMetaData2[key] = value;
                    }

                    merged = new Channel()
                    {
                        Id = this.Id,
                        Name = channel.Name,
                        Group = channel.Group,
                        Unit = channel.Unit,
                        Metadata = mergedMetaData2,
                        Datasets = mergedDatasets
                    };

                    break;

                default:
                    throw new NotSupportedException();
            }

            return merged;
        }

        public string GetPath()
        {
            return $"{this.Catalog.GetPath()}/{this.Id}";
        }

        public void Initialize()
        {
            foreach (var dataset in this.Datasets)
            {
                dataset.Channel = this;
            }
        }

        #endregion
    }
}