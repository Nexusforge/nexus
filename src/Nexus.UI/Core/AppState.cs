using System.ComponentModel;
using Microsoft.JSInterop;
using Nexus.Api;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Core;

public interface IAppState : INotifyPropertyChanged
{
    IList<AuthenticationSchemeDescription> AuthenticationSchemes { get; }

    TimeSpan SamplePeriod { get; set; }
    ExportParameters ExportParameters { get; set; }
    SettingsViewModel Settings { get; }
    IReadOnlyList<CatalogItemViewModel> SelectedCatalogItems { get; }

    ResourceCatalogViewModel RootCatalog { get; }
    ResourceCatalogViewModel? SelectedCatalog { get; set; }
    SortedDictionary<string, List<CatalogItemViewModel>>? CatalogItemsMap { get; }
    List<CatalogItemViewModel>? CatalogItems { get; set; }

    string? SearchString { get; set; }

    Task SelectCatalogAsync(string? catalogId);
    bool IsSelected(CatalogItemViewModel catalogItem);
    void ToggleCatalogItemSelection(CatalogItemViewModel catalogItem);
}

public class AppState : IAppState
{
    #region Events

    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Fields

    private List<CatalogItemViewModel> _selectedCatalogItems = new List<CatalogItemViewModel>();
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
    public IReadOnlyList<CatalogItemViewModel> SelectedCatalogItems => _selectedCatalogItems;

    public ResourceCatalogViewModel RootCatalog { get; }
    public ResourceCatalogViewModel? SelectedCatalog { get; set; }
    public SortedDictionary<string, List<CatalogItemViewModel>>? CatalogItemsMap { get; private set; }
    public List<CatalogItemViewModel>? CatalogItems { get; set; }

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
            CatalogItemsMap = null;
            CatalogItems = null;
        }

        else
        {
            CatalogItemsMap = GroupCatalogItems(SelectedCatalog.Catalog);
            CatalogItems = CatalogItemsMap?.Values.FirstOrDefault();
        }
    }

    public bool IsSelected(CatalogItemViewModel catalogItem)
    {
        return TryFindCatalogItem(catalogItem) is not null;
    }

    public void ToggleCatalogItemSelection(CatalogItemViewModel catalogItem)
    {
        var reference = TryFindCatalogItem(catalogItem);

        if (reference is null)
            _selectedCatalogItems.Add(catalogItem);
        
        else
            _selectedCatalogItems.Remove(reference);

        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCatalogItems)));
    }

    private CatalogItemViewModel? TryFindCatalogItem(CatalogItemViewModel catalogItem)
    {
        return SelectedCatalogItems.FirstOrDefault(current => 
            current.Catalog.Id == catalogItem.Catalog.Id &&
            current.Resource.Id == catalogItem.Resource.Id &&
            current.Representation.SamplePeriod == catalogItem.Representation.SamplePeriod);
    }

    private SortedDictionary<string, List<CatalogItemViewModel>>? GroupCatalogItems(ResourceCatalog catalog)
    {
        if (catalog.Resources is null)
            return null;

        var catalogItemsMap = new SortedDictionary<string, List<CatalogItemViewModel>>();

        foreach (var resource in catalog.Resources)
        {
            if (resource.Representations is null)
                continue;

            List<string> groupNames;

            if (resource.Properties is null)
            {
                groupNames = new List<string>() { "General" };
            }

            else
            {
                groupNames = resource.Properties
                    .Where(entry => entry.Key.StartsWith(GROUP_KEY + ":"))
                    .Select(entry => entry.Value)
                    .ToList();
            }

            if (!groupNames.Any())
                groupNames = new List<string>() { "General" };

            foreach (var groupName in groupNames)
            {
                var success = catalogItemsMap.TryGetValue(groupName, out var group);

                if (!success)
                {
                    group = new List<CatalogItemViewModel>();
                    catalogItemsMap[groupName] = group;
                }

                foreach (var representation in resource.Representations)
                {
                    group!.Add(new CatalogItemViewModel(catalog, resource, representation));
                }
            }
        }

        return catalogItemsMap;
    }

    #endregion
}