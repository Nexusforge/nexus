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

        internal Channel Merge(Channel channel, MergeMode mergeMode)
        {
            // merge datasets
            var mergedDatasets = new List<Dataset>();

            foreach (var dataset in channel.Datasets)
            {
                var referenceDataset = mergedDatasets.FirstOrDefault(current => current.Id == dataset.Id);

                if (referenceDataset is not null)
                {
                    switch (mergeMode)
                    {
                        case MergeMode.ExclusiveOr:

                            throw new Exception($"There may be only a single dataset with a given identifier.");

                        case MergeMode.NewWins:

                            if (!dataset.Equals(referenceDataset))
                                throw new Exception($"The datasets to be merged are not equal.");

                            break;

                        default:

                            throw new NotSupportedException($"The merge mode '{mergeMode}' is not supported.");
                    }
                }
                else
                {
                    mergedDatasets.Add(dataset with { });
                }
            }

            // merge properties
            Channel merged;

            switch (mergeMode)
            {
                case MergeMode.ExclusiveOr:

                    var mergedMetadata1 = this.Metadata
                        .ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var (key, value) in channel.Metadata)
                    {
                        if (mergedMetadata1.ContainsKey(key))
                            throw new Exception($"The left channel's metadata already contains the key '{key}'.");

                        else
                            mergedMetadata1[key] = value;
                    }

                    merged = new Channel()
                    {
                        Id = this.Id,
                        Name = string.IsNullOrWhiteSpace(this.Name) ? channel.Name : this.Name,
                        Group = string.IsNullOrWhiteSpace(this.Group) ? channel.Group : this.Group,
                        Unit = string.IsNullOrWhiteSpace(this.Unit) ? channel.Unit : this.Unit,
                        Metadata = mergedMetadata1,
                        Datasets = mergedDatasets
                    };

                    break;

                case MergeMode.NewWins:

                    var mergedMetadata2 = this.Metadata
                        .ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var (key, value) in channel.Metadata)
                    {
                        mergedMetadata2[key] = value;
                    }

                    merged = new Channel()
                    {
                        Id = this.Id,
                        Name = channel.Name,
                        Group = channel.Group,
                        Unit = channel.Unit,
                        Metadata = mergedMetadata2,
                        Datasets = mergedDatasets
                    };

                    break;

                default:
                    throw new NotSupportedException($"The merge mode '{mergeMode}' is not supported.");
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