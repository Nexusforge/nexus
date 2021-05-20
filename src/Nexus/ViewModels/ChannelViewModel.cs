using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.ViewModels
{
    public class ChannelViewModel
    {
        #region Fields

        private Channel _channel;
        private ChannelMeta _channelMeta;

        #endregion

        #region Constructors

        public ChannelViewModel(Channel channel, ChannelMeta channelMeta)
        {
            _channel = channel;
            _channelMeta = channelMeta;

            this.Datasets = channel.Datasets
                .Where(dataset => !dataset.Id.EndsWith("_status"))
                .Select(dataset => new DatasetViewModel(dataset, this)).ToList();
        }

        #endregion

        #region Properties

        public Guid Id => _channel.Id;

        public string Name => _channel.Name;

        public string Group => _channel.Group;

        public string Unit
        {
            get
            { 
                return !string.IsNullOrWhiteSpace(_channelMeta.Unit)
                    ? _channelMeta.Unit
                    : _channel.Unit;
            }
            set 
            {
                _channelMeta.Unit = value; 
            }
        }

        public string Description
        {
            get
            {
                return !string.IsNullOrWhiteSpace(_channelMeta.Description)
                    ? _channelMeta.Description
                    : _channel.Description;
            }
            set
            {
                _channelMeta.Description = value;
            }
        }

        public string SpecialInfo
        {
            get { return _channelMeta.SpecialInfo; }
            set { _channelMeta.SpecialInfo = value; }
        }

        public Project Parent => (Project)_channel.Project;

        public List<DatasetViewModel> Datasets { get; private set; }

        #endregion
    }
}
