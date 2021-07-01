using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Nexus.DataModel;
using Nexus.Roslyn;
using Nexus.Services;
using Nexus.ViewModels;
using Nexus.Infrastructure;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Options;
using Nexus.Utilities;

namespace Nexus.Core
{
    public class UserState : BindableBase, IDisposable
    {
        #region Fields

        private DateTime _dateTimeBeginWorkaround;
        private DateTime _dateTimeEndWorkaround;

        private string _searchString;
        private string _downloadMessage;
        private string _sampleRate;

        private double _downloadProgress;
        private double _visualizeProgress;

        private bool _isEditEnabled;
        private bool _visualizeBeginAtZero;

        private ClientState _clientState;
        private IJSRuntime _jsRuntime;
        private IServiceProvider _serviceProvider;
        private AppState _appState;
        private DataService _dataService;
        private UserIdService _userIdService;
        private PathsOptions _pathsOptions;
        private IDatabaseManager _databaseManager;
        private ExportParameters _exportParameters;
        private CatalogContainer _catalogContainer;
        private CodeDefinitionViewModel _codeDefinition;
        private JobControl<ExportJob> _exportJobControl;
        private AuthenticationStateProvider _authenticationStateProvider;

        private KeyValuePair<string, List<ChannelViewModel>> _groupedChannelsEntry;
        private Dictionary<string, List<DatasetViewModel>> _sampleRateToSelectedDatasetsMap;

        #endregion

        #region Constructors

        public UserState(ILogger<UserState> logger,
                         IJSRuntime jsRuntime,
                         IServiceProvider serviceProvider,
                         AppState appState,
                         UserIdService userIdService,
                         AuthenticationStateProvider authenticationStateProvider,
                         IDatabaseManager databaseManager,
                         IOptions<PathsOptions> pathsOptions,
                         DataService dataService)
        {
            this.Logger = logger;

            _jsRuntime = jsRuntime;
            _serviceProvider = serviceProvider;
            _appState = appState;
            _userIdService = userIdService;
            _authenticationStateProvider = authenticationStateProvider;
            _databaseManager = databaseManager;
            _pathsOptions = pathsOptions.Value;
            _dataService = dataService;
            _codeDefinition = this.CreateCodeDefinition(CodeType.Filter);

            this.VisualizeBeginAtZero = true;
            this.SampleRateValues = new List<string>();
            this.ExportParameters = new ExportParameters();

            _appState.PropertyChanged += this.OnAppStatePropertyChanged;

            if (_appState.IsDatabaseInitialized)
                this.Initialize(_databaseManager.Database);
        }

        #endregion

        #region Properties - General

        public ILogger<UserState> Logger { get; }

        public bool IsEditEnabled
        {
            get { return _isEditEnabled; }
            set
            {
#warning Make this more efficient. Maybe by tracking changes.
                if (_isEditEnabled && !value)
                {
                    _databaseManager.Database.CatalogContainers.ForEach(catalogContainer =>
                    {
                        _databaseManager.SaveCatalogMeta(catalogContainer.CatalogSettings);
                    });
                }

                this.SetProperty(ref _isEditEnabled, value);
            }
        }

        public ClientState ClientState
        {
            get { return _clientState; }
            set { this.SetProperty(ref _clientState, value); }
        }

        public double DownloadProgress
        {
            get { return _downloadProgress; }
            set { this.SetProperty(ref _downloadProgress, value); }
        }

        public string DownloadMessage
        {
            get { return _downloadMessage; }
            set { this.SetProperty(ref _downloadMessage, value); }
        }

        public ExportParameters ExportParameters
        {
            get
            {
                return _exportParameters;
            }
            set
            {
                _exportParameters = value;

                // Pretend that UTC time is local time to avoid conversion to nonsense.
                this.DateTimeBeginWorkaround = DateTime.SpecifyKind(value.Begin, DateTimeKind.Local);

                // Pretend that UTC time is local time to avoid conversion to nonsense.
                this.DateTimeEndWorkaround = DateTime.SpecifyKind(value.End, DateTimeKind.Local);

            }
        }

        #endregion

        #region Properties - Settings

        // this is required because MatBlazor converts dates to local representation which is not desired
        public DateTime DateTimeBeginWorkaround
        {
            get
            {
                return _dateTimeBeginWorkaround;
            }
            set
            {
                Task.Run(async () =>
                {
                    // Browser thinks it is local, but it is UTC.
                    if (value.Kind == DateTimeKind.Local)
                    {
                        _dateTimeBeginWorkaround = value;
                        this.DateTimeBegin = DateTime.SpecifyKind(value, DateTimeKind.Utc);
                    }
                    // Browser thinks it is UTC, but it is nonsense. Trying to revert that.
                    else
                    {
                        // Sending nonsense to GetBrowserTimeZoneOffset has the (small)
                        // risk of getting the wrong time zone offset back 
                        var timeZoneOffset = TimeSpan.FromMinutes(await _jsRuntime.GetBrowserTimeZoneOffset(value));

                        // Pretend that UTC time is local time to avoid conversion to nonsense again.
                        _dateTimeBeginWorkaround = DateTime.SpecifyKind(value.Add(-timeZoneOffset), DateTimeKind.Local);

                        // Correct nonsense to get back original UTC value.
                        this.DateTimeBegin = DateTime.SpecifyKind(value.Add(-timeZoneOffset), DateTimeKind.Utc);
                    }

                    if (this.DateTimeBegin >= this.DateTimeEnd)
                    {
                        // Pretend that UTC time is local time to avoid conversion to nonsense again.
                        _dateTimeEndWorkaround = DateTime.SpecifyKind(this.DateTimeBegin, DateTimeKind.Local);

                        this.DateTimeEnd = this.DateTimeBegin;
                    }
                });
            }
        }

        public DateTime DateTimeEndWorkaround
        {
            get
            {
                return _dateTimeEndWorkaround;
            }
            set
            {
                Task.Run(async () =>
                {
                    // Browser thinks it is local, but it is UTC.
                    if (value.Kind == DateTimeKind.Local)
                    {
                        _dateTimeEndWorkaround = value;
                        this.DateTimeEnd = DateTime.SpecifyKind(value, DateTimeKind.Utc);
                    }
                    // Browser thinks it is UTC, but it is nonsense. Trying to revert that.
                    else
                    {
                        // Sending nonsense to GetBrowserTimeZoneOffset has the (small)
                        // risk of getting the wrong time zone offset back 
                        var timeZoneOffset = TimeSpan.FromMinutes(await _jsRuntime.GetBrowserTimeZoneOffset(value));

                        // Pretend that UTC time is local time to avoid conversion to nonsense again.
                        _dateTimeEndWorkaround = DateTime.SpecifyKind(value.Add(-timeZoneOffset), DateTimeKind.Local);

                        // Correct nonsense to get back original UTC value.
                        this.DateTimeEnd = DateTime.SpecifyKind(value.Add(-timeZoneOffset), DateTimeKind.Utc);
                    }

                    if (this.DateTimeEnd <= this.DateTimeBegin)
                    {
                        // Pretend that UTC time is local time to avoid conversion to nonsense again.
                        _dateTimeBeginWorkaround = DateTime.SpecifyKind(this.DateTimeEnd, DateTimeKind.Local);

                        this.DateTimeBegin = this.DateTimeEnd;
                    }
                });
            }
        }

        public List<string> SampleRateValues { get; set; }

        public bool VisualizeBeginAtZero
        {
            get { return _visualizeBeginAtZero; }
            set { this.SetProperty(ref _visualizeBeginAtZero, value); }
        }

        public string SampleRate
        {
            get { return _sampleRate; }
            set { this.SetProperty(ref _sampleRate, value); }
        }

        #endregion

        #region Properties - Channel Selection

        public CatalogContainer CatalogContainer
        {
            get
            {
                return _catalogContainer;
            }
            set
            {
                // When database is updated and then the selected catalog is changed
                // "value" refers to an old catalog container that does not exist in 
                // the database anymore.
                if (value != null && !_databaseManager.Database.CatalogContainers.Contains(value))
                    value = _databaseManager.Database.CatalogContainers.FirstOrDefault(container => container.Id == value.Id);

                this.SetProperty(ref _catalogContainer, value);

                _searchString = string.Empty;

                if (this.CatalogContainersInfo.Accessible.Contains(value))
                {
                    this.UpdateGroupedChannels();
                    this.UpdateAttachments();
                }
                else
                {
                    this.GroupedChannels = null;
                    this.Attachments = null;
                }
            }
        }

        public List<string> Attachments { get; private set; }

        public SplittedCatalogContainers CatalogContainersInfo { get; private set; }

        public Dictionary<string, List<ChannelViewModel>> GroupedChannels { get; private set; }

        public KeyValuePair<string, List<ChannelViewModel>> GroupedChannelsEntry
        {
            get { return _groupedChannelsEntry; }
            set { base.SetProperty(ref _groupedChannelsEntry, value); }
        }

        public string SearchString
        {
            get { return _searchString; }
            set
            {
                base.SetProperty(ref _searchString, value);
                this.UpdateGroupedChannels();
            }
        }

        #endregion

        #region Properties - Download Area

        public IReadOnlyCollection<DatasetViewModel> SelectedDatasets => this.GetSelectedDatasets();

        #endregion

        #region Properties - Visualization

        #region Properties - FilterEditor

        public CodeDefinitionViewModel CodeDefinition
        {
            get { return _codeDefinition; }
            set { base.SetProperty(ref _codeDefinition, value); }
        }

        #endregion

        public double VisualizeProgress
        {
            get { return _visualizeProgress; }
            set { this.SetProperty(ref _visualizeProgress, value); }
        }

        #endregion

        #region Properties - Relay Properties

        public DateTime DateTimeBegin
        {
            get { return this.ExportParameters.Begin; }
            set
            {
                this.ExportParameters.Begin = value;
                this.RaisePropertyChanged();
            }
        }

        public DateTime DateTimeEnd
        {
            get { return this.ExportParameters.End; }
            set
            {
                this.ExportParameters.End = value;
                this.RaisePropertyChanged();
            }
        }

        public FileGranularity FileGranularity
        {
            get { return this.ExportParameters.FileGranularity; }
            set 
            {
                this.ExportParameters.FileGranularity = value;
                this.RaisePropertyChanged();
            }
        }

        public FileFormat FileFormat
        {
            get { return this.ExportParameters.FileFormat; }
            set
            {
                this.ExportParameters.FileFormat = value;
                this.RaisePropertyChanged();
            }
        }

        #endregion

        #region Methods

        public void SetCodeDefinitionSilently(CodeDefinitionViewModel codeDefinition)
        {
            _codeDefinition = codeDefinition;
        }

        public CodeDefinitionViewModel CreateCodeDefinition(CodeType codeType)
        {
            var baseName = VariableNameGenerator.Generate();

            var name = baseName + codeType switch
            {
                CodeType.Filter => "Filter",
                CodeType.Shared => "Shared",
                _ => throw new Exception($"The code type '{codeType}' is not supported.")
            };

            var code = codeType switch
            {
                CodeType.Filter => RoslynProject.DefaultFilterCode,
                CodeType.Shared => RoslynProject.DefaultSharedCode,
                _ => throw new Exception($"The code type '{codeType}' is not supported.")
            };

            var owner = _userIdService.User.Identity.Name;

            return new CodeDefinitionViewModel(new CodeDefinition(owner: owner))
            {
                CodeType = codeType,
                Code = code,
                Name = name,
                SampleRate = "1 s"
            };
        }

        public List<string> GetPresets()
        {
            var folderPath = Path.Combine(_pathsOptions.Data, "PRESETS");

            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            return Directory.EnumerateFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly).ToList();
        }

        public bool CanDownload()
        {
            if (this.SampleRate != null)
            {
                var samplePeriod = new SampleRateContainer(this.SampleRate).Period.TotalSeconds;


                return this.DateTimeBegin < this.DateTimeEnd &&
                       this.SelectedDatasets.Count > 0 &&
                       (ulong)this.FileGranularity >= samplePeriod;
            }
            else
            {
                return false;
            }
        }

        public async Task DownloadAsync()
        {
            EventHandler<ProgressUpdatedEventArgs> eventHandler = (sender, e) =>
            {
                this.DownloadMessage = e.Message;
                this.DownloadProgress = e.Progress;
            };

            try
            {
                this.ClientState = ClientState.PrepareDownload;
                _dataService.Progress.ProgressChanged += eventHandler;

                var selectedDatasets = this.GetSelectedDatasets().Select(dataset => dataset.Model).ToList();

                // security check
                var catalogIds = selectedDatasets.Select(dataset => dataset.Channel.Catalog.Id).Distinct();

                foreach (var catalogId in catalogIds)
                {
                    if (!NexusUtilities.IsCatalogAccessible(_userIdService.User, catalogId, _databaseManager.Database))
                        throw new UnauthorizedAccessException($"The current user is not authorized to access catalog '{catalogId}'.");
                }

                //
                var job = new ExportJob()
                {
                    Owner = _userIdService.User.Identity.Name,
                    Parameters = this.ExportParameters
                };

                var exportJobService = _serviceProvider.GetRequiredService<JobService<ExportJob>>();

                _exportJobControl = exportJobService.AddJob(job, _dataService.Progress, (jobControl, cts) =>
                {
                    var task = _dataService.ExportDataAsync(this.ExportParameters,
                                                            selectedDatasets,
                                                            cts.Token);

                    return task;
                });

                var downloadLink = await _exportJobControl.Task;

                if (!string.IsNullOrWhiteSpace(downloadLink))
                {
                    var fileName = downloadLink.Split("/").Last();
                    await _jsRuntime.FileSaveAs(fileName, downloadLink);
                }
            }
            finally
            {
                _dataService.Progress.ProgressChanged -= eventHandler;
                this.ClientState = ClientState.Normal;
                this.DownloadMessage = string.Empty;
                this.DownloadProgress = 0;
            }
        }

        public void CancelDownload()
        {
            _exportJobControl?.CancellationTokenSource.Cancel();
        }

        public void ToggleAvailability()
        {
            if (this.ClientState != ClientState.Availability)
                this.ClientState = ClientState.Availability;
            else
                this.ClientState = ClientState.Normal;
        }

        public bool CanVisualize()
        {
            return this.SelectedDatasets.Any()
                && this.DateTimeBegin < this.DateTimeEnd;
        }

        public bool IsSizeLimitExceeded()
        {
            return this.GetByteCount() > 20 * 1000 * 1000;
        }

        public void ToggleDataVisualization()
        {
            if (this.ClientState != ClientState.DataVisualizing)
                this.ClientState = ClientState.DataVisualizing;
            else
                this.ClientState = ClientState.Normal;
        }

        [JSInvokable]
        public void SetVisualizeProgress(double progress)
        {
            this.VisualizeProgress = progress;
        }

        public async Task<AvailabilityResult[]> GetAvailabilityAsync(AvailabilityGranularity granularity, CancellationToken cancellationToken)
        {
            // security check
            if (!NexusUtilities.IsCatalogAccessible(_userIdService.User, this.CatalogContainer.Id, _databaseManager.Database))
                throw new UnauthorizedAccessException($"The current user is not authorized to access catalog '{this.CatalogContainer.Id}'.");

            return await _dataService.GetAvailabilityAsync(this.CatalogContainer.Id, this.DateTimeBegin, this.DateTimeEnd, granularity, cancellationToken);
        }

        public void SetExportParameters(ExportParameters exportParameters)
        {
            _sampleRateToSelectedDatasetsMap = this.SampleRateValues
                .ToDictionary(sampleRate => sampleRate, sampleRate => new List<DatasetViewModel>());

            // find sample rate
            var sampleRates = exportParameters.ChannelPaths.Select(channelPath 
                => new SampleRateContainer(channelPath.Split("/").Last()).ToUnitString());

            if (sampleRates.Any())
                this.SampleRate = sampleRates.First();

            //
            this.ExportParameters = exportParameters;
            var selectedDatasets = this.GetSelectedDatasets();

            exportParameters.ChannelPaths.ForEach(value =>
            {
                var pathSegments = value.Split('/');
                var catalogName = $"/{pathSegments[1]}/{pathSegments[2]}/{pathSegments[3]}";
                var channelName = pathSegments[4];
                var datasetName = pathSegments[5];

                var catalogContainer = this.CatalogContainersInfo.Accessible.FirstOrDefault(current => current.Id == catalogName);

                if (catalogContainer != null)
                {
                    var channels = _appState.GetChannels(catalogContainer);
                    var channel = channels.FirstOrDefault(current => current.Id.ToString() == channelName);

                    if (channel != null)
                    {
                        var dataset = channel.Datasets.FirstOrDefault(current => current.Name == datasetName);

                        if (dataset != null)
                            selectedDatasets.Add(dataset);
                    }
                }
            });

            this.RaisePropertyChanged(nameof(UserState.ExportParameters));
        }

        public bool IsDatasetSeleced(DatasetViewModel dataset)
        {
            return this.SelectedDatasets.Contains(dataset);
        }

        public void ToggleDatasetSelection(DatasetViewModel dataset)
        {
            var isSelected = this.SelectedDatasets.Contains(dataset);

            if (isSelected)
                this.GetSelectedDatasets().Remove(dataset);
            else
                this.GetSelectedDatasets().Add(dataset);

            this.UpdateExportParameters();
            this.RaisePropertyChanged(nameof(UserState.SelectedDatasets));
        }

        public long GetByteCount()
        {
            var totalDays = (this.DateTimeEnd - this.DateTimeBegin).TotalDays;
            var frequency = string.IsNullOrWhiteSpace(this.SampleRate) ? 0 : new SampleRateContainer(this.SampleRate).SamplesPerDay;

            return (long)this.GetSelectedDatasets().Sum(dataset =>
            {
                var elementSize = NexusCoreUtilities.SizeOf(dataset.DataType);

                return frequency * totalDays * elementSize;
            });
        }

        private void Initialize(NexusDatabase database)
        {
            var catalogContainers = database.CatalogContainers;

            this.CatalogContainersInfo = this.SplitCampainContainersAsync(catalogContainers, database).Result;

            // this triggers a search to find the new container instance
            this.CatalogContainer = this.CatalogContainer;

            this.SampleRateValues = this.CatalogContainersInfo.Accessible.SelectMany(catalogContainer =>
            {
                return catalogContainer.Catalog.Channels.SelectMany(channel =>
                {
                    return channel.Datasets.Select(dataset => dataset.Id.Split('_')[0]);
                });
            }).Distinct().OrderBy(x => x, new SampleRateStringComparer()).ToList();

            // to rebuilt list with new dataset instances
            this.SetExportParameters(this.ExportParameters);

            // maybe there is a new channel available now: display it
            this.UpdateGroupedChannels();
        }

        private void UpdateAttachments()
        {
            this.Attachments = null;

            if (this.CatalogContainer != null)
            {
                var folderPath = Path.Combine(_pathsOptions.Data, "ATTACHMENTS", this.CatalogContainer.PhysicalName);

                if (Directory.Exists(folderPath))
                    this.Attachments = Directory.GetFiles(folderPath, "*").ToList();
            }
        }

        private void UpdateGroupedChannels()
        {
            if (this.CatalogContainer is not null)
            {
                this.GroupedChannels = new Dictionary<string, List<ChannelViewModel>>();

                foreach (var channel in _appState.GetChannels(this.CatalogContainer))
                {
                    if (this.ChannelMatchesFilter(channel))
                    {
                        var groupNames = channel.Group.Split('\n');

                        foreach (string groupName in groupNames)
                        {
                            var success = this.GroupedChannels.TryGetValue(groupName, out var group);

                            if (!success)
                            {
                                group = new List<ChannelViewModel>();
                                this.GroupedChannels[groupName] = group;
                            }

                            group.Add(channel);
                        }
                    }
                }

                foreach (var entry in this.GroupedChannels)
                {
                    entry.Value.Sort((x, y) => x.Name.CompareTo(y.Name));
                }

                if (this.GroupedChannels.Any())
                {
                    // try find previously selected group
                    if (this.GroupedChannelsEntry.Value is not null)
                        this.GroupedChannelsEntry = this.GroupedChannels
                            .FirstOrDefault(entry => entry.Key == this.GroupedChannelsEntry.Key);

                    // otherwise select first group
                    if (this.GroupedChannelsEntry.Value is null)
                        this.GroupedChannelsEntry = this.GroupedChannels
                            .OrderBy(entry => entry.Key)
                            .First();
                }
                else
                    this.GroupedChannelsEntry = default;
            }
            else
            {
                this.GroupedChannelsEntry = default;
            }
        }

        private void UpdateExportParameters()
        {
            this.ExportParameters.ChannelPaths = this.GetSelectedDatasets().Select(dataset =>
            {
                return $"{dataset.Parent.Parent.Id}/{dataset.Parent.Id}/{dataset.Name}";
            }).ToList();
        }

        private bool ChannelMatchesFilter(ChannelViewModel channel)
        {
            if (string.IsNullOrWhiteSpace(this.SearchString))
                return true;

            if (channel.Name.Contains(this.SearchString, StringComparison.OrdinalIgnoreCase) 
             || channel.Description.Contains(this.SearchString, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private List<DatasetViewModel> GetSelectedDatasets()
        {
            var containsKey = !string.IsNullOrWhiteSpace(this.SampleRate) && _sampleRateToSelectedDatasetsMap.ContainsKey(this.SampleRate);

            if (containsKey)
                return _sampleRateToSelectedDatasetsMap[this.SampleRate];
            else
                return new List<DatasetViewModel>();
        }

        private async Task<SplittedCatalogContainers> SplitCampainContainersAsync(List<CatalogContainer> catalogContainers,
                                                                                  NexusDatabase database)
        {
            var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
            var principal = authState.User;

            var accessible = catalogContainers.Where(catalogContainer =>
            {
                var isCatalogAccessible = NexusUtilities.IsCatalogAccessible(principal, catalogContainer.Id, database);
                var isCatalogVisible = NexusUtilities.IsCatalogVisible(principal, catalogContainer.CatalogSettings, isCatalogAccessible);

                return isCatalogAccessible && isCatalogVisible;
            }).OrderBy(catalogContainer => catalogContainer.Id).ToList();

            var restricted = catalogContainers.Where(catalogContainer =>
            {
                var isCatalogAccessible = NexusUtilities.IsCatalogAccessible(principal, catalogContainer.Id, database);
                var isCatalogVisible = NexusUtilities.IsCatalogVisible(principal, catalogContainer.CatalogSettings, isCatalogAccessible);

                return !isCatalogAccessible && isCatalogVisible;
            }).OrderBy(catalogContainer => catalogContainer.Id).ToList();

            return new SplittedCatalogContainers(accessible, restricted);
        }

        #endregion

        #region Callbacks

        public void OnAppStatePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppState.IsDatabaseInitialized) ||
               (e.PropertyName == nameof(AppState.IsDatabaseUpdating) && !this._appState.IsDatabaseUpdating))
                this.Initialize(_databaseManager.Database);
        }

        #endregion

        #region Records

        public record SplittedCatalogContainers(List<CatalogContainer> Accessible, List<CatalogContainer> Restricted);

        #endregion

        #region IDisposable Support

        private bool disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _exportJobControl?.CancellationTokenSource.Cancel();
                    _appState.PropertyChanged -= this.OnAppStatePropertyChanged;
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }
}
