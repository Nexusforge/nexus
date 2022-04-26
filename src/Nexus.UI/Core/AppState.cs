using Nexus.Api;

namespace Nexus.UI.Core;

public interface IAppState
{
    TimeSpan SamplePeriod { get; set; }
    ResourceCatalog SelectedCatalog { get; set; }
    List<ResourceCatalog> Catalogs { get; set; }
    List<CatalogItemSelection> SelectedCatalogItems { get; set; }
    ExportParameters ExportParameters { get; set; }
    IList<AuthenticationSchemeDescription> AuthenticationSchemes { get; set; }
    IList<ExtensionDescription> ExtensionDescriptions { get; set; }

    void SelectCatalog(string? catalogId);
}

public class AppState : IAppState
{
    #region Constructors

    public AppState()
    {
        // catalog 1
        var representations1_1 = new List<Representation>() 
        {
            new Representation(NexusDataType.FLOAT64, TimeSpan.FromSeconds(1), default),
            new Representation(NexusDataType.FLOAT64, TimeSpan.FromMilliseconds(1), default)
        };

        var resource1_1 = new Resource("temperature_1", default, representations1_1);
        var resources1 = new List<Resource>() { resource1_1 };
        var catalog1 = new ResourceCatalog("/LEVEL1/CATALOG/A", default, resources1);

        // catalog 2
        var representations2_1 = new List<Representation>() 
        {
            new Representation(NexusDataType.FLOAT64, TimeSpan.FromSeconds(1), default),
        };

        var resource2_1 = new Resource("wind_speed_1", default, representations2_1);
        var resources2 = new List<Resource>() { resource2_1 };
        var catalog2 = new ResourceCatalog("/LEVEL1/CATALOG/B", default, resources2);

        // catalog 3
        var representations3_1 = new List<Representation>() 
        {
            new Representation(NexusDataType.FLOAT64, TimeSpan.FromSeconds(1), default),
        };

        var resource3_1 = new Resource("P1", default, representations3_1);
        var resources3 = new List<Resource>() { resource3_1 };
        var catalog3 = new ResourceCatalog("/LEVEL2/A", default, resources3);

        // catalogs
        Catalogs = new List<ResourceCatalog>() { catalog1, catalog2, catalog3 };

        // selected catalog items
        SelectedCatalogItems = new List<CatalogItemSelection>()
        {
            new CatalogItemSelection(catalog1, resource1_1, representations1_1[1]),
            new CatalogItemSelection(catalog2, resource2_1, representations1_1[0])
        };

        // export parameters
        ExportParameters = new ExportParameters(
            Begin: DateTime.UtcNow.Date.AddDays(-2),
            End: DateTime.UtcNow.Date.AddDays(-1),
            FilePeriod: default,
            Type: string.Empty,
            ResourcePaths: new List<string>(),
            Configuration: new Dictionary<string, string>()
        );
    }

    #endregion

    #region Properties

    public TimeSpan SamplePeriod { get; set; } = TimeSpan.FromSeconds(1);

    public ResourceCatalog? SelectedCatalog { get; set; }

    public List<ResourceCatalog> Catalogs { get; set; }

    public List<CatalogItemSelection> SelectedCatalogItems { get; set; }

    public ExportParameters ExportParameters { get; set; }

    public IList<AuthenticationSchemeDescription> AuthenticationSchemes { get; set; }

    public IList<ExtensionDescription> ExtensionDescriptions { get; set; } = default!;

    #endregion

    #region Methods

    public void SelectCatalog(string? catalogId)
    {
        if (catalogId is null)
            SelectedCatalog = null;

        else
            SelectedCatalog = Catalogs
                .FirstOrDefault(catalog => catalog.Id == catalogId);
    }

    #endregion
}