using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Id,nq}")]
    public record Resource
    {
        #region Fields

        private static Regex _idValidator = new Regex(@"^[a-zA-Z][a-zA-Z0-9_]*$");

        private IReadOnlyDictionary<string, string>? _properties;
        private IReadOnlyList<Representation>? _representations;

        #endregion

        #region Constructors

        public Resource(string id, IReadOnlyDictionary<string, string>? properties = null, IReadOnlyList<Representation>? representations = null)
        {
            if (!_idValidator.IsMatch(id))
                throw new ArgumentException($"The resource identifier '{id}' is not valid.");

            this.Id = id;

            _properties = properties;
            _representations = representations;
        }

        #endregion

        #region Properties

        public string Id { get; }

        public IReadOnlyDictionary<string, string>? Properties
        {
            get => _properties;
            init => _properties = value;
        }

        public IReadOnlyList<Representation>? Representations
        {
            get => _representations;
            init => _representations = value;
        }

        #endregion

        #region "Methods"

        internal Resource Merge(Resource resource, MergeMode mergeMode)
        {
            if (this.Id != resource.Id)
                throw new ArgumentException("The resources to be merged have different identifiers.");

            // merge representations
            var uniqueIds = resource.Representations
                .Select(current => current.Id)
                .Distinct();

            if (uniqueIds.Count() != resource.Representations.Count)
                throw new ArgumentException("There are multiple representations with the same identifier.");

            var mergedRepresentations = this.Representations
               .Select(representation => representation.DeepCopy())
               .ToList();

            foreach (var representation in resource.Representations)
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

                            throw new NotSupportedException($"The merge mode '{mergeMode}' is not supported.");
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

                    var mergedProperties1 = this.Properties
                        .ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var (key, value) in resource.Properties)
                    {
                        if (mergedProperties1.ContainsKey(key))
                            throw new Exception($"The left resource has already the property '{key}'.");

                        else
                            mergedProperties1[key] = value;
                    }

                    merged = resource with
                    { 
                        Representations = mergedRepresentations, 
                        Properties = mergedProperties1 
                    };

                    break;

                case MergeMode.NewWins:

                    var mergedProperties2 = this.Properties
                        .ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var (key, value) in resource.Properties)
                    {
                        mergedProperties2[key] = value;
                    }

                    merged = resource with
                    {
                        Representations = mergedRepresentations,
                        Properties = mergedProperties2
                    };

                    break;

                default:
                    throw new NotSupportedException($"The merge mode '{mergeMode}' is not supported.");
            }

            return merged;
        }

        internal Resource DeepCopy()
        {
            return new Resource(
                id: this.Id,
                representations: this.Representations.Select(representation => representation.DeepCopy()).ToList(),
                properties: this.Properties.ToDictionary(entry => entry.Key, entry => entry.Value));
        }

        #endregion
    }
}