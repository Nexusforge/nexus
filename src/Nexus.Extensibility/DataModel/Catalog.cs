﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Nexus.DataModel
{
    [DebuggerDisplay("{Id,nq}")]
    public record Catalog
    {
        #region Properties

        public string Id { get; init; }
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
        public List<Resource> Resources { get; init; } = new List<Resource>();

        #endregion

        #region "Methods"

        public Catalog Merge(Catalog catalog, MergeMode mergeMode)
        {
            if (this.Id != catalog.Id)
                throw new Exception("The catalog to be merged has a different ID.");

            // merge resources
            var mergedResources = new List<Resource>();

            foreach (var resource in catalog.Resources)
            {
                var referenceResource = mergedResources.FirstOrDefault(current => current.Id == resource.Id);

                if (referenceResource != null)
                {
                    mergedResources.Add(referenceResource.Merge(resource, mergeMode));
                }
                else
                {
                    mergedResources.Add(resource with
                    {
                        Metadata = resource.Metadata.ToDictionary(entry => entry.Key, entry => entry.Value),
                        Representations = resource.Representations.ToList()
                    });
                }
            }

            // merge properties
            Catalog merged;

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

                    merged = new Catalog()
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

                    merged = new Catalog()
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

        public bool TryFind(string representationPath, out RepresentationRecord representationRecord, bool includeName = false)
        {
            representationRecord = null;

            var pathParts = representationPath.Split("/");
            var catalogId = string.Join('/', pathParts.Take(pathParts.Length - 2));
            var resourceId = Guid.Parse(pathParts[4]);
            var representationId = pathParts[5];

            if (catalogId != this.Id)
                return false;

            var resource = this.Resources.FirstOrDefault(resource => resource.Id == resourceId);

            if (resource is null)
            {
                if (includeName)
                    resource = this.Resources.FirstOrDefault(resource => resource.Name == pathParts[4]);

                if (resource is null)
                    return false;
            }

            var representation = resource.Representations.FirstOrDefault(representation => representation.Id == representationId);

            if (representation is null)
                return false;

            representationRecord = new RepresentationRecord(this, resource, representation);
            return true;
        }

        public RepresentationRecord Find(string representationPath, bool includeName = false)
        {
            if (!this.TryFind(representationPath, out var representationRecord, includeName))
                throw new Exception($"The representation on path '{representationPath}' could not be found.");

            return representationRecord;
        }

        public static RepresentationRecord Find(string representationPath, IEnumerable<Catalog> catalogs, bool includeName = false)
        {
            var representationRecord = default(RepresentationRecord);

            foreach (var catalog in catalogs)
            {
                if (catalog.TryFind(representationPath, out representationRecord, includeName))
                    break;
            }

            if (representationRecord is null)
                throw new Exception($"The representation on path '{representationPath}' could not be found.");

            return representationRecord;
        }

        #endregion
    }
}
