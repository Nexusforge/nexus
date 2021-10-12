using System.Collections.Generic;

namespace Nexus.DataModel
{
    public record ResourceCatalogBuilder
    {
        #region Fields

        private string _id;
        private Dictionary<string, string> _properties;
        private List<Resource> _resources;

        #endregion

        #region Constructors

        public ResourceCatalogBuilder(string id)
        {
            _id = id;
        }

        #endregion

        #region "Methods"

        public ResourceCatalogBuilder WithProperty(string key, string value)
        {
            if (_properties is null)
                _properties = new Dictionary<string, string>();

            _properties[key] = value;

            return this;
        }

        public ResourceCatalogBuilder AddResource(Resource resource)
        {
            if (_resources is null)
                _resources = new List<Resource>();

            _resources.Add(resource);

            return this;
        }

        public ResourceCatalogBuilder AddResources(params Resource[] resources)
        {
            return this.AddResources((IEnumerable<Resource>)resources);
        }

        public ResourceCatalogBuilder AddResources(IEnumerable<Resource> resources)
        {
            if (_resources is null)
                _resources = new List<Resource>();

            _resources.AddRange(resources);

            return this;
        }

        public ResourceCatalog Build()
        {
            return new ResourceCatalog(_id, _properties, _resources);
        }

        #endregion
    }
}
