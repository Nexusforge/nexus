using Microsoft.JSInterop;
using Nexus.Api;
using Nexus.UI.Core;

namespace Nexus.UI.ViewModels;

public class SettingsViewModel
{
    private IAppState _appState;
    private INexusClient _client;
    private IJSInProcessRuntime _jSInProcessRuntime;

    private const string OPTIONS_KEY = "UI:Options";
    private const string FORMAT_NAME_KEY = "UI:FormatName";
    private const string TYPE_KEY = "Type";

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
        }
    }

    public Period SamplePeriod 
    {
        get => new Period(_appState.SamplePeriod);
        set => _appState.SamplePeriod = value.Value == default 
            ? TimeSpan.FromSeconds(1) 
            : value.Value;
    }

    public Period FilePeriod 
    {
        get => new Period(_appState.ExportParameters.FilePeriod);
        set => _appState.ExportParameters = _appState.ExportParameters with { FilePeriod = value.Value };
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
    
    public IList<ExtensionDescription>? ExtensionDescriptions { get; private set; }
    public Dictionary<string, string> Items { get; private set; } = default!;
    public Dictionary<string, Dictionary<string, string>>? Options { get; private set; }

    public Lazy<Task> InitializeTask { get; }

    public Task ExportAsync()
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(_appState.ExportParameters));
        return Task.CompletedTask;

        // return Client.Jobs.ExportAsync(AppState.ExportParameters, CancellationToken.None);
    }

    public Dictionary<string, string> GetOptionItems(Dictionary<string, string> items)
    {
        return items
            .Where(entry => entry.Key.StartsWith("KeyValueMap"))
            .ToDictionary(entry => string.Join(':', entry.Key.Split(':').Skip(2)), entry => entry.Value);
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