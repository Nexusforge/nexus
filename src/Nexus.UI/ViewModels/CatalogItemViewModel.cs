using Nexus.Api;

namespace Nexus.UI.ViewModels;

public class CatalogItemViewModel
{
    private const string DESCRIPTION_KEY = "Description";
    private const string WARNING_KEY = "Warning";
    private const string UNIT_KEY = "Unit";

    public CatalogItemViewModel(ResourceCatalog resourceCatalog, Resource resource, Representation representation)
    {
        Catalog = resourceCatalog;
        Resource = resource;
        Representation = representation;

        if (resource.Properties is not null)
        {
            resource.Properties.TryGetValue(DESCRIPTION_KEY, out Description);
            resource.Properties.TryGetValue(WARNING_KEY, out Warning);
            resource.Properties.TryGetValue(UNIT_KEY, out Unit);
        }
    }

    public ResourceCatalog Catalog { get; }

    public Resource Resource { get; }

    public Representation Representation { get; }

    public string? Description;
    public string? Warning;
    public string? Unit;
}