using Nexus.DataModel;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.ViewModels
{
    public class ResourceViewModel
    {
        #region Fields

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

        public string Id => _model.Id;

        public string Description => _model.Properties.GetValueOrDefault(DataModelExtensions.Description, string.Empty);

        public string Warning => _model.Properties.GetValueOrDefault(DataModelExtensions.Warning, string.Empty);

        public string Unit => _model.Properties.GetValueOrDefault(DataModelExtensions.Unit, string.Empty);

        public IEnumerable<string> Groups
        {
            get
            {
                return _model.Properties
                    .Where(entry => entry.Key.StartsWith(DataModelExtensions.Groups))
                    .Select(entry => entry.Value.Split(':').Last());
            }
        }

        public IReadOnlyList<Representation>? Representations => _model.Representations;

        public ResourceCatalogViewModel Catalog => _catalog;

        #endregion
    }
}
