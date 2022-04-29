using Nexus.Api;
using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public abstract class ResourceCatalogViewModel
{
    public const string ROOT_CATALOG_ID = "/";

    private IAppState _appState;

    public ResourceCatalogViewModel(string id, string parentId, IAppState appState)
    {
        Id = id;
        DisplayName = Utilities.ToSpaceFilledCatalogId(id.Substring(parentId.Length));

        _appState = appState;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public bool IsSelected { get; private set; }

    public bool IsOpen { get; set; }

    public List<ResourceCatalogViewModel>? Children { get; private set; }

    protected Lazy<Task<List<ResourceCatalogViewModel>>> ChildrenTask { get; set; } = default!;

    protected Lazy<Task<ResourceCatalog>> CatalogTask { get; set; } = default!;

    public Lazy<Task<CatalogTimeRange>> TimeRangeTask { get; set; } = default!;

    public Lazy<Task<IList<string>>> AttachmentsTask { get; set; } = default!;

    public async Task SelectCatalogAsync(string catalogId)
    {
        IsSelected = catalogId == Id;

        var isOpen = false;

        if (IsSelected)
        {
            Children = await ChildrenTask.Value;
            isOpen = !IsOpen;
            _appState.SelectedCatalog = this;
        }

        if (Children is null && catalogId.StartsWith(Id))
            Children = await ChildrenTask.Value;

        if (Children is not null)
        {
            foreach (var child in Children)
            {
                await child.SelectCatalogAsync(catalogId);
                    isOpen |= child.IsOpen;
            }
        }
        
        IsOpen = isOpen;
    }
}