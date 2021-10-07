using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Name,nq}")]
    public record Resource
    {
        #region Fields

        private static Regex _idValidator = new Regex(@"^[a-zA-Z][a-zA-Z0-9_]*$");

        private string _id;

        #endregion

        #region Properties

        public string Id
        {
            get
            {
                return _id;
            }
            init
            {
                if (!_idValidator.IsMatch(value))
                    throw new ArgumentException($"The resource identifier '{value}' is not valid.");

                _id = value;
            }
        }

        public string? Unit { get; init; }

        public string? Description { get; init; }

        public string[]? Groups { get; init; }

        public List<Representation>? Representations { get; init; }

        public Dictionary<string, string>? Metadata { get; set; }

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

                    var mergedMetadata1 = this.Metadata
                        .ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var (key, value) in resource.Metadata)
                    {
                        if (mergedMetadata1.ContainsKey(key))
                            throw new Exception($"The left resource's metadata already contains the key '{key}'.");

                        else
                            mergedMetadata1[key] = value;
                    }

                    if (!string.IsNullOrWhiteSpace(this.Id) &&
                        !string.IsNullOrWhiteSpace(resource.Id) &&
                        this.Id != resource.Id)
                        throw new Exception("The resources cannot be merged because their names differ.");

                    if (!string.IsNullOrWhiteSpace(this.Unit) &&
                        !string.IsNullOrWhiteSpace(resource.Unit) &&
                        this.Unit != resource.Unit)
                        throw new Exception("The resources cannot be merged because their units differ.");

                    if (!string.IsNullOrWhiteSpace(this.Description) &&
                       !string.IsNullOrWhiteSpace(resource.Description) &&
                       this.Description != resource.Description)
                        throw new Exception("The resources cannot be merged because their descriptions differ.");

                    if (this.Groups is not null && resource.Groups is not null &&
                        this.Groups.SequenceEqual(resource.Groups))
                        throw new Exception("The resources cannot be merged because their groups differ.");

                    merged = new Resource()
                    {
                        Id = string.IsNullOrWhiteSpace(this.Id) ? resource.Id : this.Id,
                        Unit = string.IsNullOrWhiteSpace(this.Unit) ? resource.Unit : this.Unit,
                        Description = string.IsNullOrWhiteSpace(this.Description) ? resource.Description : this.Description,
                        Groups = this.Groups is null ? resource.Groups : this.Groups,
                        Metadata = mergedMetadata1,
                        Representations = mergedRepresentations
                    };

                    break;

                case MergeMode.NewWins:

                    var mergedMetadata2 = this.Metadata
                        .ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var (key, value) in resource.Metadata)
                    {
                        mergedMetadata2[key] = value;
                    }

                    merged = new Resource()
                    {
                        Id = resource.Id,
                        Unit = resource.Unit,
                        Description = resource.Description,
                        Groups = resource.Groups,
                        Metadata = mergedMetadata2,
                        Representations = mergedRepresentations
                    };

                    break;

                default:
                    throw new NotSupportedException($"The merge mode '{mergeMode}' is not supported.");
            }

            return merged;
        }

        internal Resource DeepCopy()
        {
            return this with
            {
                Metadata = this.Metadata.ToDictionary(entry => entry.Key, entry => entry.Value),
                Representations = this.Representations.Select(representation => representation.DeepCopy()).ToList()
            };
        }

        #endregion
    }
}