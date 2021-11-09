using Nexus.DataModel;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.ViewModels
{
    internal class ResourceViewModel
    {
        #region Fields

        private List<RepresentationViewModel> _representations;
        private ResourceCatalogViewModel _catalog;
        private Resource _model;

        #endregion

        #region Constructors

        public ResourceViewModel(ResourceCatalogViewModel catalog, Resource model)
        {
            _catalog = catalog;
            _model = model;
        }

        #endregion

        #region Properties

        public Resource Model => _model;

        public string Id => _model.Id;

        public string Description => _model.Properties.GetValueOrDefault(DataModelExtensions.Description, string.Empty);

        public string Warning => _model.Properties.GetValueOrDefault(DataModelExtensions.Warning, string.Empty);

        public string Unit => _model.Properties.GetValueOrDefault(DataModelExtensions.Unit, string.Empty);

        public IEnumerable<RepresentationViewModel> Representations
        {
            get
            {
                if (_representations is null && _model.Representations is not null)
                    _representations = _model.Representations
                        .Select(representation => new RepresentationViewModel(this, representation))
                        .ToList();

                return _representations ?? Enumerable.Empty<RepresentationViewModel>();
            }
        }

        public IEnumerable<string> Groups
        {
            get
            {
                return _model.Properties
                    .Where(entry => entry.Key.StartsWith(DataModelExtensions.Groups))
                    .Select(entry => entry.Value.Split(':').Last());
            }
        }

        public ResourceCatalogViewModel Catalog => _catalog;

        #endregion
    }
}
