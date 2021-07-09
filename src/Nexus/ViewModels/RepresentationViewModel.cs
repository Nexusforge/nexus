using Nexus.DataModel;

namespace Nexus.ViewModels
{
    public class RepresentationViewModel
    {
        #region Constructors

        public RepresentationViewModel(Representation representation, ResourceViewModel parent)
        {
            this.Model = representation;
            this.Parent = parent;
        }

        #endregion

        #region Properties

        public Representation Model { get; }

        public string Name => this.Model.Id;

        public NexusDataType DataType => this.Model.DataType;

        public ResourceViewModel Parent { get; }

        #endregion
    }
}
