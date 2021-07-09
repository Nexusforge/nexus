using Nexus.DataModel;
using Nexus.Infrastructure;

namespace Nexus.ViewModels
{
    public class DatasetViewModel
    {
        #region Constructors

        public DatasetViewModel(Dataset dataset, ResourceViewModel parent)
        {
            this.Model = dataset;
            this.Parent = parent;
        }

        #endregion

        #region Properties

        public Dataset Model { get; }

        public string Name => this.Model.Id;

        public NexusDataType DataType => this.Model.DataType;

        public ResourceViewModel Parent { get; }

        #endregion
    }
}
