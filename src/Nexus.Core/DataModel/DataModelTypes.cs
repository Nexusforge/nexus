using Nexus.Extensibility;
using Nexus.PackageManagement;
using System;
using System.Collections.Generic;

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

    internal record CatalogMetadata()
    {
        public string Contact { get; init; }
        public bool IsQualityControlled { get; init; }
        public bool IsHidden { get; init; }
        public string[]? GroupMemberships { get; init; }
        public ResourceCatalog? Overrides { get; init; }
    }

    public record AvailabilityResult(BackendSource BackendSource, Dictionary<DateTime, double> Data);

    public record TimeRangeResult(BackendSource BackendSource, DateTime Begin, DateTime End);
}
