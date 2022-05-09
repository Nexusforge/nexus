using Nexus.Api;

namespace Nexus.UI.ViewModels;

public class CatalogItemSelectionViewModel
{
    public CatalogItemSelectionViewModel(CatalogItemViewModel baseItem)
    {
        BaseItem = baseItem;
    }

    public CatalogItemViewModel BaseItem { get; }
    public List<RepresentationKind> Kinds { get; } = new List<RepresentationKind>();
}