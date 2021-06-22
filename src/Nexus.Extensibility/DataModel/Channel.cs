using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Serialization;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Name,nq}")]
    public class Channel
    {
        #region "Constructors"

        public Channel(Guid id, Catalog catalog)
        {
            this.Id = id;
            this.Catalog = catalog;
        }

        private Channel()
        {
            //
        }

        #endregion

        #region "Properties"

        public Guid Id { get; init; }

        public string Name { get; set; }

        public string Group { get; set; }

        public string? Unit { get; set; }

        public string? Description { get; set; }

        public string? SpecialInfo { get; set; }

        public Dictionary<string, string>? Metadata { get; set; }

        public List<Dataset> Datasets { get; set; } = new List<Dataset>();

        [JsonIgnore]
        public Catalog Catalog { get; internal set; }

        #endregion

        #region "Methods"

        public void Merge(Channel channel, ChannelMergeMode mergeMode)
        {
            if (this.Catalog.Id != channel.Catalog.Id)
                throw new Exception("The channel to be merged has a different parent.");

            // merge properties
            switch (mergeMode)
            {
                case ChannelMergeMode.OverwriteMissing:
                    
                    if (string.IsNullOrWhiteSpace(this.Name))
                        this.Name = channel.Name;

                    if (string.IsNullOrWhiteSpace(this.Group))
                        this.Group = channel.Group;

                    if (string.IsNullOrWhiteSpace(this.Unit))
                        this.Unit = channel.Unit;

                    if (string.IsNullOrWhiteSpace(this.Description))
                        this.Description = channel.Description;

                    break;

                case ChannelMergeMode.NewWins:
                    this.Name = channel.Name;
                    this.Group = channel.Group;
                    this.Unit = channel.Unit;
                    this.Description = channel.Description;
                    break;

                default:
                    throw new NotSupportedException();
            }

            // merge datasets
            var newDatasets = new List<Dataset>();

            foreach (var dataset in channel.Datasets)
            {
                var referenceDataset = this.Datasets.FirstOrDefault(current => current.Id == dataset.Id);

                if (referenceDataset != null)
                    referenceDataset.Merge(dataset);
                else
                    newDatasets.Add(dataset);

                dataset.Channel = this;
            }

            this.Datasets.AddRange(newDatasets);
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
