using Nexus.Api;
using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public class CatalogItemSelectionViewModel
{

    public CatalogItemSelectionViewModel(CatalogItemViewModel baseItem)
    {
        BaseItem = baseItem;
    }

    public CatalogItemViewModel BaseItem { get; }
    public List<RepresentationKind> Kinds { get; } = new List<RepresentationKind>();

    public string GetResourcePath(RepresentationKind kind, TimeSpan samplePeriod)
    {
        var baseItem = BaseItem;
        var samplePeriodString = Utilities.ToUnitString(samplePeriod, withUnderScore: true);
        var baseSamplePeriodString = Utilities.ToUnitString(baseItem.Representation.SamplePeriod, withUnderScore: true);
        var snakeCaseKind = Utilities.KindToString(kind);
        var representationId = $"{samplePeriodString}{snakeCaseKind}";
        var resourcePath = $"{baseItem.Catalog.Id}/{baseItem.Resource.Id}/{representationId}#base={baseSamplePeriodString}";

        return resourcePath;
    }

    public bool IsValid(Period samplePeriod)
    {
        return Kinds.All(kind => IsValid(kind, samplePeriod));
    }

    public bool IsValid(RepresentationKind kind, Period samplePeriod)
    {
        var baseSamplePeriod = BaseItem.Representation.SamplePeriod;

        return kind switch
        {
            RepresentationKind.Resampled => 
                samplePeriod.Value < baseSamplePeriod && 
                baseSamplePeriod.Ticks % samplePeriod.Value.Ticks == 0,

            RepresentationKind.Original =>
                samplePeriod.Value == baseSamplePeriod,

            _ =>
                baseSamplePeriod < samplePeriod.Value && 
                samplePeriod.Value.Ticks % baseSamplePeriod.Ticks == 0
        };
    }
}