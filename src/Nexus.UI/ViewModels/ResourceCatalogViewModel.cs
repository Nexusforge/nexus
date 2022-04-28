using System.ComponentModel;
using System.Runtime.CompilerServices;
using Nexus.Api;
using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public class ResourceCatalogViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public const string ROOT_CATALOG_ID = "/";

    private List<ResourceCatalogViewModel>? _children;
    private bool _isSelected;
    private bool _isOpen;
    private IAppState _appState;
    private Lazy<Task<List<ResourceCatalogViewModel>>> _childrenTask;

    public ResourceCatalogViewModel(string id, INexusClient client, IAppState appState)
    {
        Id = id;
        _appState = appState;

        Func<Task<List<ResourceCatalogViewModel>>> func = async () => 
        {
            var childIds = await client.Catalogs.GetChildCatalogIdsAsync(id, CancellationToken.None);
            return childIds.Select(childId => new ResourceCatalogViewModel(childId, client, appState)).ToList();
        };

        _childrenTask = new Lazy<Task<List<ResourceCatalogViewModel>>>(func);
        CatalogTask = new Lazy<Task<ResourceCatalog>>(() => client.Catalogs.GetAsync(id, CancellationToken.None));
        TimeRangeTask = new Lazy<Task<CatalogTimeRange>>(() => client.Catalogs.GetTimeRangeAsync(id, CancellationToken.None));
    }

    public string Id { get; }

    public bool IsSelected
    {  
        get
        {  
            return _isSelected;
        }  

        set
        {  
            if (value != _isSelected)
            {  
                _isSelected = value;
                // OnPropertyChanged();
            }
        }
    }

    public bool IsOpen
    {  
        get
        {  
            return _isOpen;
        }  

        set
        {  
            if (value != _isOpen)
            {  
                _isOpen = value;
                // OnPropertyChanged();
            }
        }
    }

    public List<ResourceCatalogViewModel>? Children
    {  
        get
        {
            return _children;
        }

        set
        {
            if (value != _children)
            {
                _children = value;
                // OnPropertyChanged();
            }
        }
    }

    public Lazy<Task<ResourceCatalog>> CatalogTask { get; set; }
    public Lazy<Task<CatalogTimeRange>> TimeRangeTask { get; set; }

    public async Task SelectCatalogAsync(string catalogId)
    {
        IsSelected = catalogId == Id;

        var isOpen = false;

        if (IsSelected)
        {
            Children = await _childrenTask.Value;
            isOpen = true;
            _appState.SelectedCatalog = this;
        }

        if (Children is null && catalogId.StartsWith(Id))
            Children = await _childrenTask.Value;

        if (Children is not null)
        {
            foreach (var child in Children)
            {
                if (catalogId.StartsWith(child.Id))
                {
                    await child.SelectCatalogAsync(catalogId);
                    isOpen |= child.IsOpen;
                }
            }
        }
        
        IsOpen = isOpen;
    }

    private void OnPropertyChanged([CallerMemberName] String propertyName = "")
    {  
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}