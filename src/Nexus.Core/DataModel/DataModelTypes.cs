using Nexus.Extensibility;
using Nexus.PackageManagement;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Nexus.DataModel
{
    internal class NexusProject
    {
        // There is only a single project file per Nexus instance so its okay to initialize arrays.
        public NexusProject(List<PackageReference>? packageReferences, List<BackendSource>? backendSources)
        {
            this.PackageReferences = packageReferences ?? new List<PackageReference>();
            this.BackendSources = backendSources ?? new List<BackendSource>();
        }

        public List<PackageReference> PackageReferences { get; init; }

        public List<BackendSource> BackendSources { get; init; }
    }

    internal enum AvailabilityGranularity
    {
        Day,
        Month
    }

    [DebuggerDisplay("{Id,nq}")]
    internal record CatalogContainer(DateTime CatalogBegin, DateTime CatalogEnd, ResourceCatalog Catalog, CatalogMetadata CatalogMetadata)
    {
        public string Id => this.Catalog.Id;

        public string PhysicalName => this.Id.TrimStart('/').Replace('/', '_');
    }

    internal record CatalogCollection(IReadOnlyList<CatalogContainer> CatalogContainers)
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

    internal record CatalogMetadata()
    {
        public bool IsQualityControlled { get; init; }
        public bool IsHidden { get; init; }
        public string[]? GroupMemberships { get; init; }
        public ResourceCatalog? Overrides { get; init; }
    }

    public record AvailabilityResult(BackendSource BackendSource, Dictionary<DateTime, double> Data);

    public record TimeRangeResult(BackendSource BackendSource, DateTime Begin, DateTime End);
}
