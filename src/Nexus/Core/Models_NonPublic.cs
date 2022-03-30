using Nexus.DataModel;

namespace Nexus.Core
{
    internal enum RepresentationKind
    {
        /* Warning: Do not simply rename because raw strings get parsed into this enum. */
        Original = 0,
        Resampled = 10,
        Mean = 20,
        MeanPolar = 30,
        Min = 40,
        Max = 50,
        Std = 60,
        Rms = 70,
        MinBitwise = 80,
        MaxBitwise = 90,
        Sum = 100
    }

    internal record CatalogItemRequest(
        CatalogItem Item, 
        CatalogContainer Container,
        RepresentationKind Kind);

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
