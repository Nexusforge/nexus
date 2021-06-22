using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Id,nq}")]
    public class Catalog
    {
        #region "Constructors"

        public Catalog(string id)
        {
            this.Id = id;
        }

        private Catalog()
        {
            //
        }

        #endregion

        #region "Properties"

        public string Id { get; }

        public Dictionary<string, string>? Metadata { get; set; }

        public List<Channel> Channels { get; set; } = new List<Channel>();

        #endregion

        #region "Methods"

        public void Merge(Catalog catalog, ChannelMergeMode mergeMode)
        {
            if (this.Id != catalog.Id)
                throw new Exception("The catalog to be merged has a different ID.");

            // merge channels
            var newChannels = new List<Channel>();

            foreach (var channel in catalog.Channels)
            {
                var referenceChannel = this.Channels.FirstOrDefault(current => current.Id == channel.Id);

                if (referenceChannel != null)
                    referenceChannel.Merge(channel, mergeMode);
                else
                    newChannels.Add(channel);

                channel.Catalog = this;
            }

            this.Channels.AddRange(newChannels);
        }

        public string GetPath()
        {
            return this.Id;
        }

        public void Initialize()
        {
            foreach (var channel in this.Channels)
            {
                channel.Catalog = this;
                channel.Initialize();
            }
        }

        #endregion
    }
}
