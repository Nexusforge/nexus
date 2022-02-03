using Nexus.DataModel;
using Nexus.PackageManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Nexus.Models
{
    internal class NexusProject
    {
        // There is only a single project file per Nexus instance so its okay to initialize arrays.
        public NexusProject(
            List<PackageReference>? packageReferences)
        {
            this.PackageReferences = packageReferences ?? new List<PackageReference>();
        }

        public List<PackageReference> PackageReferences { get; init; }
    }

    internal class UserConfiguration
    {
        // There are only a few user config files per Nexus instance so its okay to initialize arrays.
        public UserConfiguration(
            string username,
            List<BackendSource>? backendSources)
        {
            this.Username = username;
            this.BackendSources = backendSources ?? new List<BackendSource>();
        }

        public string Username { get; init; }
        public List<BackendSource> BackendSources { get; init; }
    }

    internal record CatalogMetadata()
    {
        public string? Contact { get; init; }
        public bool IsHidden { get; init; }
        public string[]? GroupMemberships { get; init; }
        public ResourceCatalog? Overrides { get; init; }
    }

    public record SetPropertiesRequest(
        string Properties);

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
