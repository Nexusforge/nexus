using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using Microsoft.JSInterop;
using Nexus.Api;
using Nexus.UI.Services;
using Nexus.UI.ViewModels;

namespace Nexus.UI.Core;

public interface IAppState : INotifyPropertyChanged
{
    ViewState ViewState { get; set; }
    IList<AuthenticationSchemeDescription> AuthenticationSchemes { get; }

    ExportParameters ExportParameters { get; set; }
    SettingsViewModel Settings { get; }

    ResourceCatalogViewModel RootCatalog { get; }
    ResourceCatalogViewModel? SelectedCatalog { get; set; }
    SortedDictionary<string, List<CatalogItemViewModel>>? CatalogItemsMap { get; }
    List<CatalogItemViewModel>? CatalogItemsGroup { get; set; }

    IReadOnlyList<(DateTime, Exception)> Errors { get; }
    bool HasUnreadErrors { get; set; }
    bool BeginAtZero { get; set; }
    string? SearchString { get; set; }

    ObservableCollection<JobViewModel> Jobs { get; }

    void AddError(Exception error);
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

    private ToastService _toastService;
    private ResourceCatalogViewModel? _selectedCatalog;
    private ViewState _viewState = ViewState.Normal;
    private ExportParameters _exportParameters;
    private INexusClient _client;
    private List<(DateTime, Exception)> _errors = new List<(DateTime, Exception)>();
    private bool _beginAtZero;
    private string? _searchString;
    private const string GROUP_KEY = "groups";

    #endregion

    #region Constructors

    public AppState(
        IList<AuthenticationSchemeDescription> authenticationSchemes, 
        INexusClient client,
        IJSInProcessRuntime jsRuntime,
        ToastService toastService)
    {
        AuthenticationSchemes = authenticationSchemes;
        _client = client;
        Settings = new SettingsViewModel(this, jsRuntime, client);
        _toastService = toastService;
    
        var childCatalogInfosTask = client.Catalogs.GetChildCatalogInfosAsync(ResourceCatalogViewModel.ROOT_CATALOG_ID, CancellationToken.None);

        var rootInfo = new CatalogInfo(
            Id: ResourceCatalogViewModel.ROOT_CATALOG_ID,
            Title: default!, 
            Contact: default, 
            License: default,
            IsReadable: true,
            IsWritable: false, 
            IsReleased: true,
            IsVisible: true,
            IsOwner: false,
            DataSourceInfoUrl: default,
            DataSourceType: default!,
            DataSourceRegistrationId: default,
            PackageReferenceId: default);

        RootCatalog = new FakeResourceCatalogViewModel(rootInfo, "", client, this, childCatalogInfosTask);

        // export parameters
        ExportParameters = new ExportParameters(
            Begin: DateTime.UtcNow.Date.AddDays(-2),
            End: DateTime.UtcNow.Date.AddDays(-1),
            FilePeriod: default,
            Type: string.Empty,
            ResourcePaths: new List<string>(),
            Configuration: default
        );
    }

    #endregion

    #region Properties

    public ViewState ViewState
    {
        get {
            return _viewState;
        }
        set
        {
            if (_viewState != value)
            {
                _viewState = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewState)));
            }
        }
    }

    public IList<AuthenticationSchemeDescription> AuthenticationSchemes { get; }
    
    public ExportParameters ExportParameters
    {
        get {
            return _exportParameters;
        }
        set
        {
            if (_exportParameters != value)
            {
                _exportParameters = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExportParameters)));
            }
        }
    }

    public SettingsViewModel Settings { get; }

    public ResourceCatalogViewModel RootCatalog { get; }

    public ResourceCatalogViewModel? SelectedCatalog
    {
        get {
            return _selectedCatalog;
        }
        set
        {
            if (_selectedCatalog != value)
            {
                _selectedCatalog = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCatalog)));
            }
        }
    }

    public SortedDictionary<string, List<CatalogItemViewModel>>? CatalogItemsMap { get; private set; }
    
    public List<CatalogItemViewModel>? CatalogItemsGroup { get; set; }

    public IReadOnlyList<(DateTime, Exception)> Errors => _errors;

    public bool HasUnreadErrors { get; set; }

    public bool BeginAtZero
    {
        get 
        {
            return _beginAtZero;
        }
        set
        {
            if (value != _beginAtZero)
            {
                _beginAtZero = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BeginAtZero)));
            }
        }
    }

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
                CatalogItemsGroup = CatalogItemsMap?.Values.FirstOrDefault();

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
            CatalogItemsGroup = default;
        }

        else
        {
            CatalogItemsMap = GroupCatalogItems(SelectedCatalog.Catalog);
            CatalogItemsGroup = CatalogItemsMap?.Values.FirstOrDefault();
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CatalogItemsMap)));

        if (SelectedCatalog is FakeResourceCatalogViewModel)
            ViewState = ViewState.Normal;
    }

    public void AddError(Exception error)
    {
        _errors.Add((DateTime.UtcNow, error));
        HasUnreadErrors = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Errors)));

        if (_toastService.Toast is not null)
            _ = _toastService.Toast.ShowAsync("An error occured.", error.Message);
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
                groupNames = new List<string>() { "default" };
            }

            else
            {
                if (resource.Properties.Value.TryGetProperty(GROUP_KEY, out var groupElement) && 
                    groupElement.ValueKind == JsonValueKind.Array)
                {
                    groupNames = groupElement
                        .EnumerateArray()
                        .Where(current => current.ValueKind == JsonValueKind.String)
                        .Select(current => current.GetString()!)
                        .ToList();
                }

                else
                {
                    groupNames = new List<string>() { "default" };
                }
            }

            if (!groupNames.Any())
                groupNames = new List<string>() { "default" };

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

        if (resource.Properties is not null && 
            resource.Properties.Value.TryGetProperty(CatalogItemViewModel.DESCRIPTION_KEY, out var descriptionElement) &&
            descriptionElement.ValueKind == JsonValueKind.String)
        {
            description = descriptionElement.GetString();
        };

        if (resource.Id.Contains(SearchString, StringComparison.OrdinalIgnoreCase) ||
            description is not null && description.Contains(SearchString, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    #endregion
}