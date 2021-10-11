using System.Collections.Generic;
using System.Diagnostics;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Id,nq}")]
    public record ResourceBuilder
    {
        #region Fields

        private string _id;
        private Dictionary<string, string> _properties;
        private List<Representation> _representations;

        #endregion

        #region Constructors

        public ResourceBuilder(string id)
        {
            _id = id;
        }

        #endregion

        #region "Methods"

        public ResourceBuilder WithProperty(string key, string value)
        {
            if (_properties is null)
                _properties = new Dictionary<string, string>();

            _properties[key] = value;

            return this;
        }

        public ResourceBuilder AddRepresentation(Representation representation)
        {
            if (_representations is null)
                _representations = new List<Representation>();

            _representations.Add(representation);

            return this;
        }

        public ResourceBuilder AddRepresentations(params Representation[] representations)
        {
            return this.AddRepresentations(representations);
        }

        public ResourceBuilder AddRepresentations(IEnumerable<Representation> representations)
        {
            if (_representations is null)
                _representations = new List<Representation>();

            _representations.AddRange(representations);

            return this;
        }

        public Resource Build()
        {
            return new Resource(_id, _properties, _representations);
        }

        #endregion
    }
}