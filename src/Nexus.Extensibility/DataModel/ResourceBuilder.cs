using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexus.DataModel
{
    /// <summary>
    /// A resource builder simplifies building a resource.
    /// </summary>
    [DebuggerDisplay("{Id,nq}")]
    public record ResourceBuilder
    {
        #region Fields

        private string _id;
        private JsonNode? _properties;
        private List<Representation>? _representations;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceBuilder"/>.
        /// </summary>
        /// <param name="id">The identifier of the resource to be built.</param>
        public ResourceBuilder(string id)
        {
            _id = id;
        }

        #endregion

        #region "Methods"

        /// <summary>
        /// Adds a property.
        /// </summary>
        /// <param name="key">The key of the property.</param>
        /// <param name="value">The value of the property.</param>
        /// <returns>The resource builder.</returns>
        public ResourceBuilder WithProperty(string key, string value)
        {
            if (_properties is null)
                _properties = JsonNode.Parse("{}")!;

            _properties[key] = value;

            return this;
        }

        /// <summary>
        /// Adds a <see cref="Representation"/>.
        /// </summary>
        /// <param name="representation">The <see cref="Representation"/>.</param>
        /// <returns>The resource builder.</returns>
        public ResourceBuilder AddRepresentation(Representation representation)
        {
            if (_representations is null)
                _representations = new List<Representation>();

            _representations.Add(representation);

            return this;
        }

        /// <summary>
        /// Adds a list of <see cref="Representation"/>.
        /// </summary>
        /// <param name="representations">The list of <see cref="Representation"/>.</param>
        /// <returns>The resource builder.</returns>
        public ResourceBuilder AddRepresentations(params Representation[] representations)
        {
            return AddRepresentations((IEnumerable<Representation>)representations);
        }

        /// <summary>
        /// Adds a list of <see cref="Representation"/>.
        /// </summary>
        /// <param name="representations">The list of <see cref="Representation"/>.</param>
        /// <returns>The resource builder.</returns>
        public ResourceBuilder AddRepresentations(IEnumerable<Representation> representations)
        {
            if (_representations is null)
                _representations = new List<Representation>();

            _representations.AddRange(representations);

            return this;
        }

        /// <summary>
        /// Builds the <see cref="Resource"/>.
        /// </summary>
        /// <returns>The <see cref="Resource"/>.</returns>
        public Resource Build()
        {
            var properties = JsonSerializer.SerializeToElement(_properties);
            return new Resource(_id, properties, _representations);
        }

        #endregion
    }
}