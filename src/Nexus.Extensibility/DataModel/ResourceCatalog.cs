﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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

        private static Regex _idValidator = new Regex(@"^(?:\/[a-zA-Z][a-zA-Z0-9_]*)+$");
        private static IReadOnlyDictionary<string, string> _emptyProperties = new Dictionary<string, string>();
        private static IReadOnlyList<Resource> _emptyResources = new List<Resource>();

        private IReadOnlyDictionary<string, string>? _properties;
        private IReadOnlyList<Resource>? _resources;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceCatalog"/>.
        /// </summary>
        /// <param name="id">The catalog identifier.</param>
        /// <param name="properties">The map of properties.</param>
        /// <param name="resources">The list of representations.</param>
        /// <exception cref="ArgumentException">Thrown when the resource identifier is not valid.</exception>
        public ResourceCatalog(string id, IReadOnlyDictionary<string, string>? properties = null, IReadOnlyList<Resource>? resources = null)
        {
            if (!_idValidator.IsMatch(id))
                throw new ArgumentException($"The resource catalog identifier '{id}' is not valid.");

            this.Id = id;

            if (resources is not null)
                this.ValidateResources(resources);

            _properties = properties;
            _resources = resources;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the identifier.
        /// </summary>
        public string Id { get; init;  }

        /// <summary>
        /// Gets the map of properties.
        /// </summary>
        public IReadOnlyDictionary<string, string>? Properties
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
                    this.ValidateResources(value);

                _resources = value;
            }
        }

        #endregion

        #region "Methods"

        /// <summary>
        /// Merges another catalog with this instance.
        /// </summary>
        /// <param name="catalog">The catalog to merge into this instance.</param>
        /// <param name="mergeMode">The <paramref name="mergeMode"/>.</param>
        /// <returns>The merged catalog.</returns>
        public ResourceCatalog Merge(ResourceCatalog catalog, MergeMode mergeMode)
        {
            if (this.Id != catalog.Id)
                throw new ArgumentException("The catalogs to be merged have different identifiers.");

            var newProperties = catalog.Properties ?? _emptyProperties;
            var newResources = catalog.Resources ?? _emptyResources;
            var thisProperties = this.Properties ?? _emptyProperties;
            var thisResources = this.Resources ?? _emptyResources;

            // merge resources
            var mergedResources = thisResources
                .Select(resource => resource.DeepCopy())
                .ToList();

            foreach (var newResource in newResources)
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
                        Properties = newResource.Properties?.ToDictionary(entry => entry.Key, entry => entry.Value),
                        Representations = newResource.Representations?.ToList()
                    });
                }
            }

            // merge properties
            ResourceCatalog merged;

            switch (mergeMode)
            {
                case MergeMode.ExclusiveOr:

                    var mergedProperties1 = thisProperties
                        .ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var (key, value) in newProperties)
                    {
                        if (mergedProperties1.ContainsKey(key))
                            throw new Exception($"The left catalog has already the property '{key}'.");

                        else
                            mergedProperties1[key] = value;
                    }

                    merged = catalog with
                    {
                        Properties = mergedProperties1.Any() ? mergedProperties1 : null,
                        Resources = mergedResources.Any() ? mergedResources : null
                    };

                    break;

                case MergeMode.NewWins:

                    var mergedProperties2 = thisProperties
                        .ToDictionary(entry => entry.Key, entry => entry.Value);

                    foreach (var (key, value) in newProperties)
                    {
                        mergedProperties2[key] = value;
                    }

                    merged = catalog with
                    {
                        Properties = mergedProperties2.Any() ? mergedProperties2 : null,
                        Resources = mergedResources.Any() ? mergedResources : null
                    };

                    break;

                default:
                    throw new NotSupportedException();
            }

            return merged;
        }

        internal bool TryFind(string resourcePath, [NotNullWhen(true)] out CatalogItem? catalogItem)
        {
            catalogItem = null;

            var pathParts = resourcePath.Split("/");
            var catalogId = string.Join('/', pathParts.Take(pathParts.Length - 2));
            var resourceId = pathParts[4];
            var representationId = pathParts[5];

            if (catalogId != this.Id)
                return false;

            var resource = this.Resources?.FirstOrDefault(resource => resource.Id == resourceId);

            if (resource is null)
                return false;

            var representation = resource.Representations?.FirstOrDefault(representation => representation.Id == representationId);

            if (representation is null)
                return false;

            catalogItem = new CatalogItem(this, resource, representation);
            return true;
        }

        internal CatalogItem Find(string resourcePath)
        {
            if (!this.TryFind(resourcePath, out var catalogItem))
                throw new Exception($"The resource path '{resourcePath}' could not be found.");

            return catalogItem;
        }

        private void ValidateResources(IReadOnlyList<Resource> resources)
        {
            var uniqueIds = resources
                .Select(current => current.Id)
                .Distinct();

            if (uniqueIds.Count() != resources.Count)
                throw new ArgumentException("There are multiple resource with the same identifier.");
        }

        #endregion
    }
}
