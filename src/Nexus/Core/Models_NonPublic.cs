using Nexus.DataModel;
using Nexus.Extensibility;
using System.IO.Pipelines;

namespace Nexus.Core
{
    internal record struct Interval(
        DateTime Begin,
        DateTime End);

    internal record ReadUnit(
        ReadRequest ReadRequest,
        CatalogItemRequest CatalogItemRequest,
        PipeWriter DataWriter);

    internal record CatalogItemRequest(
        CatalogItem Item,
        CatalogItem? BaseItem,
        CatalogContainer Container);

    internal record NexusProject(
        IReadOnlyDictionary<string, string> SystemConfiguration,
        IReadOnlyDictionary<Guid, PackageReference> PackageReferences,
        IReadOnlyDictionary<string, UserConfiguration> UserConfigurations);

    internal record UserConfiguration(
        IReadOnlyDictionary<Guid, DataSourceRegistration> DataSourceRegistrations);

    internal record CatalogState(
        CatalogContainer Root,
        CatalogCache Cache);

    internal record LazyCatalogInfo(
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

        public Task<object?> Task { get; set; } = default!;

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
