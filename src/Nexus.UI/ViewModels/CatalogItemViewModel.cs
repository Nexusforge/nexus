using System.Text.Json;
using Nexus.Api;

namespace Nexus.UI.ViewModels;

public class CatalogItemViewModel
{
    public const string DESCRIPTION_KEY = "Description";
    private const string WARNING_KEY = "Warning";
    private const string UNIT_KEY = "Unit";

    public CatalogItemViewModel(ResourceCatalog resourceCatalog, Resource resource, Representation representation)
    {
        Catalog = resourceCatalog;
        Resource = resource;
        Representation = representation;

        if (resource.Properties.HasValue)
        {
            if (resource.Properties.Value.TryGetProperty(DESCRIPTION_KEY, out var descriptionElement) && 
                descriptionElement.ValueKind == JsonValueKind.String)
            {
                Description = descriptionElement.GetString();
            }

            if (resource.Properties.Value.TryGetProperty(WARNING_KEY, out var warningElement) && 
                warningElement.ValueKind == JsonValueKind.String)
            {
                Warning = descriptionElement.GetString();
            }

            if (resource.Properties.Value.TryGetProperty(WARNING_KEY, out var unitElement) && 
                unitElement.ValueKind == JsonValueKind.String)
            {
                Unit = descriptionElement.GetString();
            }
        }
    }

    public ResourceCatalog Catalog { get; }

    public Resource Resource { get; }

    public Representation Representation { get; }

    public string? Description;
    public string? Warning;
    public string? Unit;
}