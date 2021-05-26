using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Id,nq}")]
    public class Project
    {
        #region "Constructors"

        public Project(string id)
        {
            this.Id = id;
        }

        private Project()
        {
            //
        }

        #endregion

        #region "Properties"

        public string Id { get; }

        public List<Channel> Channels { get; set; } = new List<Channel>();

        #endregion

        #region "Methods"

        public void Merge(Project project, ChannelMergeMode mergeMode)
        {
            if (this.Id != project.Id)
                throw new Exception("The project to be merged has a different ID.");

            // merge channels
            var newChannels = new List<Channel>();

            foreach (var channel in project.Channels)
            {
                var referenceChannel = this.Channels.FirstOrDefault(current => current.Id == channel.Id);

                if (referenceChannel != null)
                    referenceChannel.Merge(channel, mergeMode);
                else
                    newChannels.Add(channel);

                channel.Project = this;
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
                channel.Project = this;
                channel.Initialize();
            }
        }

        #endregion
    }
}
