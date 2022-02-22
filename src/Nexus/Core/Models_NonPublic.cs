using Nexus.DataModel;

namespace Nexus.Core
{
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
        Dictionary<CatalogContainer, IEnumerable<CatalogItem>> CatalogItemsMap,
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
            this.Progress = e;
            this.ProgressUpdated?.Invoke(this, e);
        }

        public void OnCompleted()
        {
            this.Completed?.Invoke(this, EventArgs.Empty);
        }
    }
}