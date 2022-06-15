using Nexus.Api;
using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public class CatalogItemViewModel
{
    public const string DESCRIPTION_KEY = "description";
    private const string WARNING_KEY = "warning";
    public const string UNIT_KEY = "unit";

    public CatalogItemViewModel(ResourceCatalog resourceCatalog, Resource resource, Representation representation)
    {
        Catalog = resourceCatalog;
        Resource = resource;
        Representation = representation;

        if (resource.Properties.HasValue)
        {
            Description = Utilities.GetPropertyStringValue(resource.Properties, DESCRIPTION_KEY);
            Warning = Utilities.GetPropertyStringValue(resource.Properties, WARNING_KEY);
            Unit = Utilities.GetPropertyStringValue(resource.Properties, UNIT_KEY);
        }
    }

    public ResourceCatalog Catalog { get; }

    public Resource Resource { get; }

    public Representation Representation { get; }

    public string? Description;
    public string? Warning;
    public string? Unit;
}