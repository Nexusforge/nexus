using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Name,nq}")]
    public record Resource
    {
        #region Properties

        public Guid Id { get; init; }
        public string Name { get; init; }
        public string Group { get; init; }
        public string Unit { get; init; }
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
        public List<Representation> Representations { get; init; } = new List<Representation>();

        #endregion

        #region "Methods"

        internal Resource Merge(Resource resource, MergeMode mergeMode)
        {
            // merge representations
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

                            throw new Exception($"There may be only a single representation with a given identifier.");

                        case MergeMode.NewWins:

                            if (!representation.Equals(mergedRepresentations[index]))
                                throw new Exception($"The representations to be merged are not equal.");

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

                    if (!string.IsNullOrWhiteSpace(this.Name) &&
                        !string.IsNullOrWhiteSpace(resource.Name) &&
                        this.Name != resource.Name)
                        throw new Exception($"The resources cannot be merged because their names differ.");

                    if (!string.IsNullOrWhiteSpace(this.Group) &&
                        !string.IsNullOrWhiteSpace(resource.Group) &&
                        this.Group != resource.Group)
                        throw new Exception($"The resources cannot be merged because their groups differ.");

                    if (!string.IsNullOrWhiteSpace(this.Unit) &&
                        !string.IsNullOrWhiteSpace(resource.Unit) &&
                        this.Unit != resource.Unit)
                        throw new Exception($"The resources cannot be merged because their units differ.");

                    merged = new Resource()
                    {
                        Id = this.Id,
                        Name = string.IsNullOrWhiteSpace(this.Name) ? resource.Name : this.Name,
                        Group = string.IsNullOrWhiteSpace(this.Group) ? resource.Group : this.Group,
                        Unit = string.IsNullOrWhiteSpace(this.Unit) ? resource.Unit : this.Unit,
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
                        Id = this.Id,
                        Name = resource.Name,
                        Group = resource.Group,
                        Unit = resource.Unit,
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