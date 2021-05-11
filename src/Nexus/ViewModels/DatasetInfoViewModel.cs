using Nexus.Database;
using Nexus.Infrastructure;

namespace Nexus.ViewModels
{
    public class DatasetInfoViewModel
    {
        #region Constructors

        public DatasetInfoViewModel(DatasetInfo dataset, ChannelInfoViewModel parent)
        {
            this.Model = dataset;
            this.Parent = parent;
        }

        #endregion

        #region Properties

        public DatasetInfo Model { get; }

        public string Name => this.Model.Id;

        public NexusDataType DataType => this.Model.DataType;

        public ChannelInfoViewModel Parent { get; }

        #endregion
    }
}
