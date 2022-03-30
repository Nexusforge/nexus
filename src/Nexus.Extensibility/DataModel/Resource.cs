using System.Diagnostics;
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
        private static IReadOnlyDictionary<string, string> _emptyProperties = new Dictionary<string, string>();
        private static IReadOnlyList<Representation> _emptyRepresentations = new List<Representation>();

        private IReadOnlyDictionary<string, string>? _properties;
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
        public Resource(string id, IReadOnlyDictionary<string, string>? properties = null, IReadOnlyList<Representation>? representations = null)
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
        public IReadOnlyDictionary<string, string>? Properties
        {
            get => _properties;
            init => _properties = value;
        }

        /// <summary>
        /// Tshe list of representations.
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

        internal Resource Merge(Resource resource, MergeMode mergeMode)
        {
            if (Id != resource.Id)
                throw new ArgumentException("The resources to be merged have different identifiers.");

            var newProperties = resource.Properties ?? _emptyProperties;
            var newRepresentations = resource.Representations ?? _emptyRepresentations;
            var thisProperties = Properties ?? _emptyProperties;
            var thisRepresentations = Representations ?? _emptyRepresentations;

            // merge representations
            var mergedRepresentations = thisRepresentations
               .Select(representation => representation.DeepCopy())
               .ToList();

            foreach (var representation in newRepresentations)
            {
                var index = mergedRepresentations.FindIndex(current => current.Id == representation.Id);

                if (index >= 0)
                {
                    switch (mergeMode)
                    {
                        case MergeMode.ExclusiveOr:

                            throw new Exception("There may be only a single representation with a given identifier.");

                        case MergeMode.NewWins:

                            if (!representation.Equals(mergedRepresentations[index]))
                                throw new Exception("The representations to be merged are not equal.");

                            break;

                        default:

                            throw new NotSupportedException($"The merge mode {mergeMode} is not supported.");
                    }
                }
                else
                {
                    mergedRepresentations.Add(representation);
                }
            }

            // merge properties
            Resource merged;

            switch (mergeMode)
            {
                case MergeMode.ExclusiveOr:

                    var mergedProperties1 = thisProperties
                        .ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var (key, value) in newProperties)
                    {
                        if (mergedProperties1.ContainsKey(key))
                            throw new Exception($"The left resource has already the property {key}.");

                        else
                            mergedProperties1[key] = value;
                    }

                    merged = resource with
                    {
                        Properties = mergedProperties1.Any() ? mergedProperties1 : null,
                        Representations = mergedRepresentations.Any() ? mergedRepresentations : null
                    };

                    break;

                case MergeMode.NewWins:

                    var mergedProperties2 = thisProperties
                        .ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var (key, value) in newProperties)
                    {
                        mergedProperties2[key] = value;
                    }

                    merged = resource with
                    {
                        Properties = mergedProperties2.Any() ? mergedProperties2 : null,
                        Representations = mergedRepresentations.Any() ? mergedRepresentations : null
                    };

                    break;

                default:
                    throw new NotSupportedException($"The merge mode {mergeMode} is not supported.");
            }

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
                    : Properties.ToDictionary(entry => entry.Key, entry => entry.Value));
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