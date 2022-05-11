using System.ComponentModel;
using Microsoft.JSInterop;
using Nexus.Api;
using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    #region Events

    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    private TimeSpan _samplePeriod { get; set; } = TimeSpan.FromSeconds(1);
    private IAppState _appState;
    private INexusClient _client;
    private IJSInProcessRuntime _jSInProcessRuntime;

    private const string OPTIONS_KEY = "UI:Options";
    private const string FORMAT_NAME_KEY = "UI:FormatName";
    private const string TYPE_KEY = "Type";

    private List<CatalogItemSelectionViewModel> _selectedCatalogItems = new List<CatalogItemSelectionViewModel>();

    public SettingsViewModel(IAppState appState, IJSInProcessRuntime jSInProcessRuntime, INexusClient client)
    {
        _appState = appState;
        _jSInProcessRuntime = jSInProcessRuntime;
        _client = client;

        InitializeTask = new Lazy<Task>(InitializeAsync);
    }

    public DateTime Begin
    {
        get
        {
            return _appState.ExportParameters.Begin;
        }
        set
        {
            _appState.ExportParameters = _appState.ExportParameters with { Begin = DateTime.SpecifyKind(value, DateTimeKind.Utc) };

            if (_appState.ExportParameters.Begin >= _appState.ExportParameters.End)
                _appState.ExportParameters = _appState.ExportParameters with { End = _appState.ExportParameters.Begin };

            CanExportChanged();
        }
    }

    public DateTime End
    {
        get
        {
            return _appState.ExportParameters.End;
        }
        set
        {
            _appState.ExportParameters = _appState.ExportParameters with { End = DateTime.SpecifyKind(value, DateTimeKind.Utc) };

            if (_appState.ExportParameters.End <= _appState.ExportParameters.Begin)
                _appState.ExportParameters = _appState.ExportParameters with { Begin = _appState.ExportParameters.End };

            CanExportChanged();
        }
    }

    public Period SamplePeriod 
    {
        get => new Period(_samplePeriod);
        set
        {
            _samplePeriod = value.Value == default 
                ? TimeSpan.FromSeconds(1) 
                : value.Value;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SamplePeriod)));
            CanExportChanged();
        }
    }

    public Period FilePeriod 
    {
        get
        {
            return new Period(_appState.ExportParameters.FilePeriod);
        }
        set
        {
            _appState.ExportParameters = _appState.ExportParameters with { FilePeriod = value.Value };
            CanExportChanged();
        }
    }

    public string FileType
    {
        get 
        {
            return _appState.ExportParameters.Type;
        }
        set
        {
            _appState.ExportParameters = _appState.ExportParameters with { Type = value };
            _jSInProcessRuntime.InvokeVoid("nexus.util.saveSetting", "nexus.ui.file-type", value);
            PrepareOptions();
        }
    }
    
    public IDictionary<string, string> Configuration
    {
        get => _appState.ExportParameters.Configuration;
    }

    public IReadOnlyList<CatalogItemSelectionViewModel> SelectedCatalogItems => _selectedCatalogItems;

    public IList<ExtensionDescription>? ExtensionDescriptions { get; private set; }
    public Dictionary<string, string> Items { get; private set; } = default!;
    public Dictionary<string, Dictionary<string, string>>? Options { get; private set; }

    public Lazy<Task> InitializeTask { get; }

    public bool CanExport
    {
        get
        {
            var result =  
                Begin < End &&
                Begin.Ticks % SamplePeriod.Value.Ticks == 0 &&
                End.Ticks % SamplePeriod.Value.Ticks == 0 &&
                SelectedCatalogItems.Any() &&
                SelectedCatalogItems.All(item => item.IsValid(SamplePeriod)) &&
                (FilePeriod.Value == TimeSpan.Zero || FilePeriod.Value.Ticks % SamplePeriod.Value.Ticks == 0);

            return result;
        }
    }

    public void CanExportChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanExport)));
    }

    public Dictionary<string, string> GetOptionItems(Dictionary<string, string> items)
    {
        return items
            .Where(entry => entry.Key.StartsWith("KeyValueMap"))
            .ToDictionary(entry => string.Join(':', entry.Key.Split(':').Skip(2)), entry => entry.Value);
    }

    public bool IsSelected(CatalogItemViewModel catalogItem)
    {
        return TryFindSelectedCatalogItem(catalogItem) is not null;
    }

    public void ToggleCatalogItemSelection(CatalogItemViewModel catalogItem)
    {
        var reference = TryFindSelectedCatalogItem(catalogItem);

        if (reference is null)
        {
            var selectedItem = new CatalogItemSelectionViewModel(catalogItem);
            EnsureDefaultRepresentationKind(selectedItem);
            _selectedCatalogItems.Add(selectedItem);
        }
        
        else
        {
            _selectedCatalogItems.Remove(reference);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCatalogItems)));
        CanExportChanged();
    }

    private CatalogItemSelectionViewModel? TryFindSelectedCatalogItem(CatalogItemViewModel catalogItem)
    {
        return SelectedCatalogItems.FirstOrDefault(current => 
            current.BaseItem.Catalog.Id == catalogItem.Catalog.Id &&
            current.BaseItem.Resource.Id == catalogItem.Resource.Id &&
            current.BaseItem.Representation.SamplePeriod == catalogItem.Representation.SamplePeriod);
    }

    private void EnsureDefaultRepresentationKind(CatalogItemSelectionViewModel selectedItem)
    {
        var baseItem = selectedItem.BaseItem;
        var baseSamplePeriod = baseItem.Representation.SamplePeriod;

        if (!selectedItem.Kinds.Any())
        {
            if (SamplePeriod.Value < baseSamplePeriod)
                selectedItem.Kinds.Add(RepresentationKind.Resampled);

            else if (SamplePeriod.Value > baseSamplePeriod)
                selectedItem.Kinds.Add(RepresentationKind.Mean);

            else
                selectedItem.Kinds.Add(RepresentationKind.Original);
        }
    }

    private async Task InitializeAsync()
    {
        var extensionDescriptions = (await _client.Writers
            .GetDescriptionsAsync(CancellationToken.None))
            .Where(description => 
                description.AdditionalInfo is not null && 
                description.AdditionalInfo.ContainsKey(FORMAT_NAME_KEY))
            .ToList();

        if (extensionDescriptions.Any())
        {
            var fileType = _jSInProcessRuntime.Invoke<string?>("nexus.util.loadSetting", "nexus.ui.file-type");
    
            _appState.ExportParameters = _appState.ExportParameters with
            { 
                Type = !string.IsNullOrWhiteSpace(fileType) && extensionDescriptions.Any(extensionDescription => extensionDescription.Type == fileType)
                    ? fileType
                    : extensionDescriptions.First().Type 
            };
        }

        Items = extensionDescriptions.ToDictionary(
            description => description.Type,
            description => description.AdditionalInfo![FORMAT_NAME_KEY]);

        ExtensionDescriptions = extensionDescriptions;

        PrepareOptions();
    }

    private void PrepareOptions()
    {
        if (ExtensionDescriptions is null || !ExtensionDescriptions.Any())
            return;

        var description = ExtensionDescriptions!
            .First(description => description.Type == FileType);

        Options = description.AdditionalInfo!
            .Where(entry => entry.Key.StartsWith(OPTIONS_KEY))
            .GroupBy(entry => entry.Key.Split(":")[2])
            .ToDictionary(
                group => group.First(entry => entry.Key.EndsWith(TYPE_KEY)).Value, 
                group => group.ToDictionary(entry => string.Join(':', entry.Key.Split(':').Skip(3)), entry => entry.Value));
    }
}