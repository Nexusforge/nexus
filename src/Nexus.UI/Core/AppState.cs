using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.JSInterop;
using Nexus.Api;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Core;

public interface IAppState : INotifyPropertyChanged
{
    IList<AuthenticationSchemeDescription> AuthenticationSchemes { get; }

    ExportParameters ExportParameters { get; set; }
    SettingsViewModel Settings { get; }

    ResourceCatalogViewModel RootCatalog { get; }
    ResourceCatalogViewModel? SelectedCatalog { get; set; }
    SortedDictionary<string, List<CatalogItemViewModel>>? CatalogItemsMap { get; }
    List<CatalogItemViewModel>? CatalogItems { get; set; }

    string? SearchString { get; set; }

    ObservableCollection<JobViewModel> Jobs { get; }
    void AddJob(JobViewModel job);
    void CancelJob(JobViewModel job);

    Task SelectCatalogAsync(string? catalogId);
}

public class AppState : IAppState
{
    #region Events

    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Fields

    private INexusClient _client;
    private string? _searchString;
    private const string GROUP_KEY = "Groups";

    #endregion

    #region Constructors

    public AppState(
        IList<AuthenticationSchemeDescription> authenticationSchemes, 
        INexusClient client,
        IJSInProcessRuntime jSInProcessRuntime)
    {
        AuthenticationSchemes = authenticationSchemes;
        _client = client;
        Settings = new SettingsViewModel(this, jSInProcessRuntime, client);

        var childCatalogInfosTask = client.Catalogs.GetChildCatalogInfosAsync(ResourceCatalogViewModel.ROOT_CATALOG_ID, CancellationToken.None);

        var rootInfo = new CatalogInfo(
            Id: ResourceCatalogViewModel.ROOT_CATALOG_ID,
            Title: default!, 
            Contact: default, 
            License: default,
            IsReadable: true,
            IsWritable: false, 
            IsPublished: true, 
            IsOwner: false);

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
    
    public ExportParameters ExportParameters { get; set; }
    public SettingsViewModel Settings { get; }

    public ResourceCatalogViewModel RootCatalog { get; }
    public ResourceCatalogViewModel? SelectedCatalog { get; set; }
    public SortedDictionary<string, List<CatalogItemViewModel>>? CatalogItemsMap { get; private set; }
    public List<CatalogItemViewModel>? CatalogItems { get; set; }

    public string? SearchString
    {
        get 
        {
            return _searchString;
        }
        set
        {
            if (value != _searchString)
            {
                _searchString = value;
                
                CatalogItemsMap = GroupCatalogItems(SelectedCatalog!.Catalog!);
                CatalogItems = CatalogItemsMap?.Values.FirstOrDefault();

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchString)));
            }
        }
    }

    public ObservableCollection<JobViewModel> Jobs { get; set; } = new ObservableCollection<JobViewModel>();

    #endregion

    #region Methods

    public void AddJob(JobViewModel job)
    {
        if (Jobs.Count >= 20)
            Jobs.RemoveAt(0);

        Jobs.Add(job);
    }

    public void CancelJob(JobViewModel job)
    {      
        if (Jobs.Count >= 20)
            Jobs.RemoveAt(0);

        if (job.Status is null || job.Status.Status < Api.TaskStatus.RanToCompletion)
            _ = _client.Jobs.CancelJobAsync(job.Id);
    }

    public async Task SelectCatalogAsync(string? catalogId)
    {
        _searchString = default;

        if (catalogId is null)
            catalogId = ResourceCatalogViewModel.ROOT_CATALOG_ID;

        await RootCatalog.SelectCatalogAsync(catalogId);

        if (SelectedCatalog is null || SelectedCatalog.Catalog is null)
        {
            CatalogItemsMap = default;
            CatalogItems = default;
        }

        else
        {
            CatalogItemsMap = GroupCatalogItems(SelectedCatalog.Catalog);
            CatalogItems = CatalogItemsMap?.Values.FirstOrDefault();
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CatalogItemsMap)));
    }

    private SortedDictionary<string, List<CatalogItemViewModel>>? GroupCatalogItems(ResourceCatalog catalog)
    {
        if (catalog.Resources is null)
            return null;

        var catalogItemsMap = new SortedDictionary<string, List<CatalogItemViewModel>>();

        foreach (var resource in catalog.Resources)
        {
            if (resource.Representations is null || !ResourceMatchesFilter(resource))
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

                if (resource.Representations is not null)
                {
                    foreach (var representation in resource.Representations)
                    {
                        group!.Add(new CatalogItemViewModel(catalog, resource, representation));
                    }
                }
            }
        }

        return catalogItemsMap;
    }

    private bool ResourceMatchesFilter(Resource resource)
    {
        if (string.IsNullOrWhiteSpace(SearchString))
            return true;

        string? description = default;

        if (resource.Properties is not null)
            resource.Properties.TryGetValue(CatalogItemViewModel.DESCRIPTION_KEY, out description);

        if (resource.Id.Contains(SearchString, StringComparison.OrdinalIgnoreCase) ||
            description is not null && description.Contains(SearchString, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    #endregion
}