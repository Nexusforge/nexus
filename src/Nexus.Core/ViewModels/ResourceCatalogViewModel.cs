using Nexus.DataModel;
using System.Collections.Generic;

namespace Nexus.ViewModels
{
    internal class ResourceCatalogViewModel
    {
        #region Fields

        private ResourceCatalog _model;

        #endregion

        #region Constructors

        public ResourceCatalogViewModel(ResourceCatalog catalog)
        {
            _model = catalog;
        }

        #endregion

        #region Properties

        public ResourceCatalog Model => _model;

        public string Id => _model.Id;

        public string Description => _model.Properties.GetValueOrDefault(DataModelExtensions.Description, string.Empty);

        public string ShortDescription => _model.Properties.GetValueOrDefault(DataModelExtensions.ShortDescription, string.Empty);

        #endregion
    }
}
