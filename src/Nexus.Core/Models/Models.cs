using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.Models
{
    internal record NexusProject(
        IReadOnlyDictionary<Guid, PackageReference> PackageReferences);

    public record PackageReference(
        string Provider,
        Dictionary<string, string> Configuration);

    internal record UserConfiguration(
        string Username,
        List<BackendSource> BackendSources);

    public record CatalogMetadata(
        string? Contact, 
        bool IsHidden, 
        string[]? GroupMemberships,
        ResourceCatalog? Overrides);

    public record AddPackageReferenceRequest(
        PackageReference PackageReference);

    public record SetMetadataRequest(
        CatalogMetadata Metadata);

    public record TimeRangeResponse(
        DateTime Begin,
        DateTime End);

    public record AvailabilityResponse(
        Dictionary<DateTime, double> Data);

    internal sealed record BackendSource(
        string Type,
        Uri ResourceLocator,
        Dictionary<string, string> Configuration,
        bool Publish,
        bool Disable = false)
    {
        public override int GetHashCode()
        {
            return HashCode.Combine(this.Type, this.ResourceLocator);
        }

        public bool Equals(BackendSource? other)
        {
            if (other is null)
                return false;

            var typeEquals = this.Type == other.Type;
            var resourceLocatorEquals = this.ResourceLocator.Equals(other.ResourceLocator);
            var configurationEquals = this.Configuration
                                          .OrderBy(entry => entry.Key)
                                          .SequenceEqual(other.Configuration.OrderBy(entry => entry.Key));

            return typeEquals && resourceLocatorEquals && configurationEquals;
        }
    }
}
