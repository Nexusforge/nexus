using Nexus.Buffers;
using Nexus.Extensibility;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.DataModel
{
    public class SparseProject : Project
    {
        #region "Constructors"

        public SparseProject(string id, ProjectLicense license) : base(id)
        {
            this.License = license;
        }

        #endregion

        #region Properties

        public ProjectLicense License { get; }

        #endregion

        #region "Methods"

        public List<ChannelDescription> ToChannelDescriptions()
        {
            return this.Channels.SelectMany(channel =>
            {
                return channel.Datasets.Select(dataset =>
                {
                    var guid = channel.Id;
                    var displayName = channel.Name;
                    var datasetName = dataset.Id;
                    var groupName = channel.Group;
                    var dataType = dataset.DataType;
                    var sampleRate = dataset.GetSampleRate();
                    var unit = channel.Unit;

                    return new ChannelDescription(guid,
                                                   displayName,
                                                   datasetName,
                                                   groupName,
                                                   dataType,
                                                   sampleRate,
                                                   unit,
                                                   BufferType.Simple);
                });
            }).ToList();
        }

        #endregion
    }
}
