using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Id,nq}")]
    public record ResourceCatalog
    {
        #region Fields

        private static Regex _idValidator = new Regex(@"^(?:\/[a-zA-Z][a-zA-Z0-9_]*)+$");

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
                    throw new ArgumentException($"The resource catalog identifier '{value}' is not valid.");

                _id = value;
            }
        }

        public Dictionary<string, string>? Metadata { get; set; }

        public List<Resource>? Resources { get; init; }

        #endregion

        #region "Methods"

        public ResourceCatalog Merge(ResourceCatalog catalog, MergeMode mergeMode)
        {
            if (this.Id != catalog.Id)
                throw new ArgumentException("The catalogs to be merged have different identifiers.");

            // merge resources
            var uniqueIds = catalog.Resources
                .Select(current => current.Id)
                .Distinct();

            if (uniqueIds.Count() != catalog.Resources.Count)
                throw new ArgumentException("There are multiple resource with the same identifier.");

            var mergedResources = this.Resources
                .Select(resource => resource.DeepCopy())
                .ToList();

            foreach (var newResource in catalog.Resources)
            {
                var index = mergedResources.FindIndex(current => current.Id == newResource.Id);

                if (index >= 0)
                {
                    mergedResources[index] = mergedResources[index].Merge(newResource, mergeMode);
                }
                else
                {
                    mergedResources.Add(newResource with
                    {
                        Metadata = newResource.Metadata.ToDictionary(entry => entry.Key, entry => entry.Value),
                        Representations = newResource.Representations.ToList()
                    });
                }
            }

            // merge properties
            ResourceCatalog merged;

            switch (mergeMode)
            {
                case MergeMode.ExclusiveOr:

                    var mergedMetadata1 = this.Metadata
                        .ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var (key, value) in catalog.Metadata)
                    {
                        if (mergedMetadata1.ContainsKey(key))
                            throw new Exception($"The left catalog's metadata already contains the key '{key}'.");

                        else
                            mergedMetadata1[key] = value;
                    }

                    merged = new ResourceCatalog()
                    {
                        Id = this.Id,
                        Metadata = mergedMetadata1,
                        Resources = mergedResources
                    };

                    break;

                case MergeMode.NewWins:

                    var mergedMetadata2 = this.Metadata
                        .ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var (key, value) in catalog.Metadata)
                    {
                        mergedMetadata2[key] = value;
                    }

                    merged = new ResourceCatalog()
                    {
                        Id = this.Id,
                        Metadata = mergedMetadata2,
                        Resources = mergedResources
                    };

                    break;

                default:
                    throw new NotSupportedException();
            }

            return merged;
        }

        public bool TryFind(string resourcePath, out CatalogItem catalogItem)
        {
            catalogItem = null;

            var pathParts = resourcePath.Split("/");
            var catalogId = string.Join('/', pathParts.Take(pathParts.Length - 2));
            var resourceId = pathParts[4];
            var representationId = pathParts[5];

            if (catalogId != this.Id)
                return false;

            var resource = this.Resources.FirstOrDefault(resource => resource.Id == resourceId);

            if (resource is null)
                return false;

            var representation = resource.Representations.FirstOrDefault(representation => representation.Id == representationId);

            if (representation is null)
                return false;

            catalogItem = new CatalogItem(this, resource, representation);
            return true;
        }

        public CatalogItem Find(string resourcePath)
        {
            if (!this.TryFind(resourcePath, out var catalogItem))
                throw new Exception($"The resource path '{resourcePath}' could not be found.");

            return catalogItem;
        }

        #endregion
    }
}
