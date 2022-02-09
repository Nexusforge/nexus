using Nexus.DataModel;

namespace Nexus.ViewModels
{
    internal class RepresentationViewModel
    {
        #region Fields

        private ResourceViewModel _resource;
        private Representation _model;

        #endregion

        #region Constructors

        public RepresentationViewModel(ResourceViewModel resource, Representation representation)
        {
            _resource = resource;
            _model = representation;
        }

        #endregion

        #region Properties

        public Representation Model => _model;

        public string Id => _model.Id;

        public TimeSpan SamplePeriod => _model.SamplePeriod;

        public ResourceViewModel Resource => _resource;

        public NexusDataType DataType => _model.DataType;

        public string GetPath()
        {
            return $"{_resource.Catalog.Id}/{_resource.Id}/{this.Id}";
        }

        #endregion
    }
}
