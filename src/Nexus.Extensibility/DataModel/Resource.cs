using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexus.DataModel
{
    /// <summary>
    /// A resource is part of a resource catalog and holds a list of representations.
    /// </summary>
    [DebuggerDisplay("{Id,nq}")]
    public record Resource
    {
        #region Fields

        private static Regex _idValidator = new Regex(@"^[a-zA-Z][a-zA-Z0-9_]*$");

        private JsonElement? _properties;
        private IReadOnlyList<Representation>? _representations;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="Resource"/>.
        /// </summary>
        /// <param name="id">The resource identifier.</param>
        /// <param name="properties">The map of properties.</param>
        /// <param name="representations">The list of representations.</param>
        /// <exception cref="ArgumentException">Thrown when the resource identifier is not valid.</exception>
        public Resource(string id, JsonElement? properties = default, IReadOnlyList<Representation>? representations = default)
        {
            if (!_idValidator.IsMatch(id))
                throw new ArgumentException($"The resource identifier {id} is not valid.");

            Id = id;

            _properties = properties;

            if (representations is not null)
                ValidateRepresentations(representations);

            _representations = representations;
        }

        #endregion

        #region Properties

        /// <summary>
        /// The identifier.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The map of properties.
        /// </summary>
        public JsonElement? Properties
        {
            get => _properties;
            init => _properties = value;
        }

        /// <summary>
        /// The list of representations.
        /// </summary>
        public IReadOnlyList<Representation>? Representations
        {
            get
            {
                return _representations;
            }

            init
            {
                if (value is not null)
                    ValidateRepresentations(value);

                _representations = value;
            }
        }

        #endregion
        
        #region "Methods"

        internal Resource Merge(Resource resource)
        {
            if (Id != resource.Id)
                throw new ArgumentException("The resources to be merged have different identifiers.");

            var mergedRepresentations = DataModelUtilities.MergeRepresentations(Representations, resource.Representations);
            var mergedProperties = DataModelUtilities.MergeProperties(Properties, resource.Properties);

            var merged = resource with
            {
                Properties = mergedProperties,
                Representations = mergedRepresentations
            };

            return merged;
        }

        internal Resource DeepCopy()
        {
            return new Resource(
                id: Id,
                representations: Representations is null 
                    ? null
                    : Representations.Select(representation => representation.DeepCopy()).ToList(),
                properties: Properties is null
                    ? null
                    : Properties.Value.Clone());
        }

        private void ValidateRepresentations(IReadOnlyList<Representation> representations)
        {
            var uniqueIds = representations
                .Select(current => current.Id)
                .Distinct();

            if (uniqueIds.Count() != representations.Count)
                throw new ArgumentException("There are multiple representations with the same identifier.");
        }

        #endregion
    }
}