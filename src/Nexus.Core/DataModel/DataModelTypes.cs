using Nexus.Extensibility;
using Nexus.PackageManagement;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Nexus.DataModel
{
    public record NexusProject()
    {
        public List<PackageReference> PackageReferences { get; set; }

        public List<BackendSource> BackendSources { get; set; }
    }

    public enum AvailabilityGranularity
    {
        Day,
        Month
    }

    [DebuggerDisplay("{Id,nq}")]
    public record CatalogContainer(DateTime CatalogBegin, DateTime CatalogEnd, ResourceCatalog Catalog, CatalogMetadata CatalogMetadata)
    {
        public string Id => this.Catalog.Id;

        public string PhysicalName => this.Id.TrimStart('/').Replace('/', '_');
    }

    public record CatalogCollection(IReadOnlyList<CatalogContainer> CatalogContainers)
    {
        public bool TryFind(string catalogId, string resourceId, string representationId, out CatalogItem catalogItem)
        {
            var resourcePath = $"{catalogId}/{resourceId}/{representationId}";
            return this.TryFind(resourcePath, out catalogItem);
        }

        public bool TryFind(string resourcePath, out CatalogItem catalogItem)
        {
            return this.CatalogContainers
                .Select(container => container.Catalog)
                .TryFind(resourcePath, out catalogItem);
        }

        public CatalogItem Find(string catalogId, string resourceId, string representationId)
        {
            if (!this.TryFind(catalogId, resourceId, representationId, out var catalogItem))
                throw new Exception($"The resource path '{catalogId}/{resourceId}/{representationId}' could not be found.");

            return catalogItem;
        }
    }

    public record CatalogMetadata()
    {
        public bool IsQualityControlled { get; init; }
        public bool IsHidden { get; init; }
        public string[] Logbook { get; init; }
        public string[] GroupMemberships { get; init; }
        public ResourceCatalog Overrides { get; init; }
    }

    public record AvailabilityResult
    {
        public BackendSource BackendSource { get; set; }
        public Dictionary<DateTime, double> Data { get; set; }
    }

    public record TimeRangeResult
    {
        public BackendSource BackendSource { get; set; }
        public DateTime Begin { get; set; }
        public DateTime End { get; set; }
    }
}
