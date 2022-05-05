using Microsoft.JSInterop;
using Nexus.Api;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Core;

public interface IAppState
{
    IList<AuthenticationSchemeDescription> AuthenticationSchemes { get; }

    TimeSpan SamplePeriod { get; set; }
    ExportParameters ExportParameters { get; set; }
    SettingsViewModel Settings { get; }
    List<CatalogItemSelection> SelectedCatalogItems { get; set; }

    ResourceCatalogViewModel RootCatalog { get; }
    ResourceCatalogViewModel? SelectedCatalog { get; set; }
    SortedDictionary<string, List<Resource>>? GroupedResources { get; }
    List<Resource>? SelectedResourceGroup { get; }

    string? SearchString { get; set; }

    Task SelectCatalogAsync(string? catalogId);
}

public class AppState : IAppState
{
    #region Fields

    private const string GROUP_KEY = "Groups";

    #endregion

    #region Constructors

    public AppState(
        IList<AuthenticationSchemeDescription> authenticationSchemes, 
        INexusClient client, 
        IJSInProcessRuntime jSInProcessRuntime)
    {
        AuthenticationSchemes = authenticationSchemes;
        Settings = new SettingsViewModel(this, jSInProcessRuntime, client);

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

    public IList<AuthenticationSchemeDescription> AuthenticationSchemes { get; }

    public TimeSpan SamplePeriod { get; set; } = TimeSpan.FromSeconds(1);
    public ExportParameters ExportParameters { get; set; }
    public SettingsViewModel Settings { get; }
    public List<CatalogItemSelection> SelectedCatalogItems { get; set; } = new List<CatalogItemSelection>();


    public ResourceCatalogViewModel RootCatalog { get; }
    public ResourceCatalogViewModel? SelectedCatalog { get; set; }
    public SortedDictionary<string, List<Resource>>? GroupedResources { get; private set; }
    public List<Resource>? SelectedResourceGroup { get; private set; }

    public string? SearchString { get; set; }

    #endregion

    #region Methods

    public async Task SelectCatalogAsync(string? catalogId)
    {
        if (catalogId is null)
            catalogId = ResourceCatalogViewModel.ROOT_CATALOG_ID;

        await RootCatalog.SelectCatalogAsync(catalogId);

        if (SelectedCatalog is null || SelectedCatalog.Catalog is null)
        {
            GroupedResources = null;
            SelectedResourceGroup = null;
        }

        else
        {
            GroupedResources = GroupResources(SelectedCatalog.Catalog);
            SelectedResourceGroup = GroupedResources?.Values.FirstOrDefault();
        }
    }

    private SortedDictionary<string, List<Resource>>? GroupResources(ResourceCatalog catalog)
    {
        if (catalog.Resources is null)
            return null;

        var groupedResources = new SortedDictionary<string, List<Resource>>();

        foreach (var resource in catalog.Resources)
        {
            List<string> groupNames;

            if (resource.Properties is null)
            {
                groupNames = new List<string>() { "General" };
            }

            else
            {
                groupNames = resource.Properties
                    .Where(entry => entry.Value.StartsWith(GROUP_KEY + ":"))
                    .Select(entry => entry.Value)
                    .ToList();
            }

            if (!groupNames.Any())
                groupNames = new List<string>() { "General" };

            foreach (var groupName in groupNames)
            {
                var success = groupedResources.TryGetValue(groupName, out var group);

                if (!success)
                {
                    group = new List<Resource>();
                    groupedResources[groupName] = group;
                }

                group!.Add(resource);
            }
        }

        return groupedResources;
    }

    #endregion
}