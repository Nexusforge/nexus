using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexus.DataModel
{
    /// <summary>
    /// A catalog is a top level element and holds a list of resources.
    /// </summary>
    [DebuggerDisplay("{Id,nq}")]
    public record ResourceCatalog
    {
        #region Fields

        private string _id = default!;
        private JsonElement? _properties;
        private IReadOnlyList<Resource>? _resources;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceCatalog"/>.
        /// </summary>
        /// <param name="id">The catalog identifier.</param>
        /// <param name="properties">The properties.</param>
        /// <param name="resources">The list of resources.</param>
        /// <exception cref="ArgumentException">Thrown when the resource identifier is not valid.</exception>
        public ResourceCatalog(string id, JsonElement? properties = default, IReadOnlyList<Resource>? resources = default)
        {
            Id = id;
            Properties = properties;
            Resources = resources;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a regular expression to validate a resource catalog identifier.
        /// </summary>
        public static Regex ValidIdExpression { get; } = new Regex(@"^(?:\/[a-zA-Z_][a-zA-Z_0-9]*)+$");

        /// <summary>
        /// Gets the identifier.
        /// </summary>
        public string Id
        {
            get
            {
                return _id;
            }

            init
            {
                if (!ValidIdExpression.IsMatch(value))
                    throw new ArgumentException($"The resource catalog identifier {value} is not valid.");

                _id = value;
            }
        }

        /// <summary>
        /// Gets the properties.
        /// </summary>
        public JsonElement? Properties
        {
            get => _properties;
            init => _properties = value;
        }

        /// <summary>
        /// Gets the list of representations.
        /// </summary>
        public IReadOnlyList<Resource>? Resources
        {
            get
            {
                return _resources;
            }

            init
            {
                if (value is not null)
                    ValidateResources(value);

                _resources = value;
            }
        }

        #endregion

        #region "Methods"

        /// <summary>
        /// Merges another catalog with this instance.
        /// </summary>
        /// <param name="catalog">The catalog to merge into this instance.</param>
        /// <returns>The merged catalog.</returns>
        public ResourceCatalog Merge(ResourceCatalog catalog)
        {
            if (Id != catalog.Id)
                throw new ArgumentException("The catalogs to be merged have different identifiers.");

            var mergedProperties = DataModelUtilities.MergeProperties(Properties, catalog.Properties);
            var mergedResources = DataModelUtilities.MergeResources(Resources, catalog.Resources);

            var merged = catalog with
            {
                Properties = mergedProperties,
                Resources = mergedResources
            };

            return merged;
        }

        internal bool TryFind(string resourcePath, [NotNullWhen(true)] out CatalogItem? catalogItem)
        {
            catalogItem = default;

            var pathParts = resourcePath.Split('/');
            var catalogId = string.Join('/', pathParts[..^2]);
            var resourceId = pathParts[^2];
            var representationId = pathParts[^1];

            if (catalogId != Id)
                return false;

            var resource = Resources?.FirstOrDefault(resource => resource.Id == resourceId);

            if (resource is null)
                return false;

            var representation = string.IsNullOrEmpty(representationId)
                ? resource.Representations?.FirstOrDefault()
                : resource.Representations?.FirstOrDefault(representation => representation.Id == representationId);

            if (representation is null)
                return false;

            catalogItem = new CatalogItem(
                this with { Resources = default },
                resource with { Representations = default },
                representation);

            return true;
        }

        internal CatalogItem Find(string resourcePath)
        {
            if (!TryFind(resourcePath, out var catalogItem))
                throw new Exception($"The resource path {resourcePath} could not be found.");

            return catalogItem;
        }

        private void ValidateResources(IReadOnlyList<Resource> resources)
        {           
            var uniqueIds = resources
                .Select(current => current.Id)
                .Distinct();

            if (uniqueIds.Count() != resources.Count)
                throw new ArgumentException("There are multiple resources with the same identifier.");
        }

        #endregion
    }
}
