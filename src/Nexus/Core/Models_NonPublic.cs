using Nexus.DataModel;
using Nexus.Extensibility;
using System.IO.Pipelines;

namespace Nexus.Core
{
    internal record struct ReadUnitSlice(
        DateTime Begin,
        DateTime End,
        int Offset,
        int Length,
        bool FromCache);

    internal record ReadUnit(
        ReadRequest ReadRequest,
        CatalogItemRequest CatalogItemRequest,
        PipeWriter DataWriter)
    {
        public ReadUnitSlice[]? Slices { get; set; }
    }

    internal record CatalogItemRequest(
        CatalogItem Item,
        CatalogItem? BaseItem,
        CatalogContainer Container);

    internal record NexusProject(
        IReadOnlyDictionary<Guid, PackageReference> PackageReferences,
        IReadOnlyDictionary<string, UserConfiguration> UserConfigurations);

    internal record UserConfiguration(
        IReadOnlyDictionary<Guid, DataSourceRegistration> DataSourceRegistrations);

    internal record CatalogState(
        CatalogContainer Root,
        CatalogCache Cache);

    internal record CatalogInfo(
        DateTime Begin,
        DateTime End, 
        ResourceCatalog Catalog);

    internal record ExportContext(
        TimeSpan SamplePeriod,
        IEnumerable<CatalogItemRequest> CatalogItemRequests,
        ExportParameters ExportParameters);

    internal record JobControl(
        DateTime Start,
        Job Job,
        CancellationTokenSource CancellationTokenSource)
    {
        public event EventHandler<double>? ProgressUpdated;
        public event EventHandler? Completed;

        public double Progress { get; private set; }

        public Task<object?> Task { get; set; } = null!;

        public void OnProgressUpdated(double e)
        {
            Progress = e;
            ProgressUpdated?.Invoke(this, e);
        }

        public void OnCompleted()
        {
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }
}
