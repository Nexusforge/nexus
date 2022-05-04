using Nexus.Api;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Core;

public interface IAppState
{
    TimeSpan SamplePeriod { get; set; }
    ResourceCatalogViewModel RootCatalog { get; }
    ResourceCatalogViewModel? SelectedCatalog { get; set; }
    List<CatalogItemSelection> SelectedCatalogItems { get; set; }
    ExportParameters ExportParameters { get; set; }
    IList<AuthenticationSchemeDescription> AuthenticationSchemes { get; set; }
    IList<ExtensionDescription> ExtensionDescriptions { get; set; }

    Task SelectCatalogAsync(string? catalogId);
}

public class AppState : IAppState
{
    #region Constructors

    public AppState(INexusClient client)
    {
        var childCatalogInfosTask = client.Catalogs.GetChildCatalogInfosAsync(ResourceCatalogViewModel.ROOT_CATALOG_ID, CancellationToken.None);

        var rootInfo = new CatalogInfo(ResourceCatalogViewModel.ROOT_CATALOG_ID, default!, default, default, true, false);
        RootCatalog = new FakeResourceCatalogViewModel(rootInfo, "", client, this, childCatalogInfosTask);

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

    public ResourceCatalogViewModel RootCatalog { get; }

    public ResourceCatalogViewModel? SelectedCatalog { get; set; }

    public List<CatalogItemSelection> SelectedCatalogItems { get; set; } = new List<CatalogItemSelection>();

    public ExportParameters ExportParameters { get; set; }

    public IList<AuthenticationSchemeDescription> AuthenticationSchemes { get; set; } = default!;

    public IList<ExtensionDescription> ExtensionDescriptions { get; set; } = default!;

    #endregion

    #region Methods

    public async Task SelectCatalogAsync(string? catalogId)
    {
        if (catalogId is null)
            catalogId = ResourceCatalogViewModel.ROOT_CATALOG_ID;

        await RootCatalog.SelectCatalogAsync(catalogId);
    }

    #endregion
}