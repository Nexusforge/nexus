using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.ViewModels
{
    public class ResourceViewModel
    {
        #region Fields

        private Resource _resource;
        private ResourceMeta _resourceMeta;

        #endregion

        #region Constructors

        public ResourceViewModel(Resource resource, ResourceMeta resourceMeta)
        {
            _resource = resource;
            _resourceMeta = resourceMeta;

            this.Representations = resource.Representations
                .Where(representation => !representation.Id.EndsWith("_status"))
                .Select(representation => new RepresentationViewModel(representation, this)).ToList();
        }

        #endregion

        #region Properties

        public Guid Id => _resource.Id;

        public string Name => _resource.Id;

        public string Group => _resource.Group;

        public string Unit
        {
            get
            { 
                return !string.IsNullOrWhiteSpace(_resourceMeta.Unit)
                    ? _resourceMeta.Unit
                    : _resource.Unit;
            }
            set 
            {
                _resourceMeta.Unit = value; 
            }
        }

        public string Description
        {
            get
            {
                return !string.IsNullOrWhiteSpace(_resourceMeta.Description)
                    ? _resourceMeta.Description
                    : _resource.Description;
            }
            set
            {
                _resourceMeta.Description = value;
            }
        }

        public string SpecialInfo
        {
            get { return _resourceMeta.SpecialInfo; }
            set { _resourceMeta.SpecialInfo = value; }
        }

        public ResourceCatalog Parent => (ResourceCatalog)_resource.Catalog;

        public List<RepresentationViewModel> Representations { get; private set; }

        #endregion
    }
}
