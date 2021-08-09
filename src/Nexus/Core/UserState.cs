using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Nexus.DataModel;
using Nexus.Roslyn;
using Nexus.Services;
using Nexus.ViewModels;
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

        private KeyValuePair<string, List<ResourceViewModel>> _groupedResourcesEntry;
        private Dictionary<string, List<RepresentationViewModel>> _sampleRateToSelectedRepresentationsMap;

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

        #region Properties - Resource Selection

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
                    this.UpdateGroupedResources();
                    this.UpdateAttachments();
                }
                else
                {
                    this.GroupedResources = null;
                    this.Attachments = null;
                }
            }
        }

        public List<string> Attachments { get; private set; }

        public SplittedCatalogContainers CatalogContainersInfo { get; private set; }

        public Dictionary<string, List<ResourceViewModel>> GroupedResources { get; private set; }

        public KeyValuePair<string, List<ResourceViewModel>> GroupedResourcesEntry
        {
            get { return _groupedResourcesEntry; }
            set { base.SetProperty(ref _groupedResourcesEntry, value); }
        }

        public string SearchString
        {
            get { return _searchString; }
            set
            {
                base.SetProperty(ref _searchString, value);
                this.UpdateGroupedResources();
            }
        }

        #endregion

        #region Properties - Download Area

        public IReadOnlyCollection<RepresentationViewModel> SelectedRepresentations => this.GetSelectedRepresentations();

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
#error Remove presets
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
                       this.SelectedRepresentations.Count > 0 &&
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
                _dataService.ReadProgress.ProgressChanged += eventHandler;

                var selectedRepresentations = this.GetSelectedRepresentations().Select(representation => representation.Model).ToList();

                // security check
                var catalogIds = selectedRepresentations.Select(representation => representation.Resource.Catalog.Id).Distinct();

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

                _exportJobControl = exportJobService.AddJob(job, _dataService.ReadProgress, (jobControl, cts) =>
                {
                    var task = _dataService.ExportDataAsync(this.ExportParameters,
                                                            selectedRepresentations,
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
                _dataService.ReadProgress.ProgressChanged -= eventHandler;
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
            return this.SelectedRepresentations.Any()
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
            _sampleRateToSelectedRepresentationsMap = this.SampleRateValues
                .ToDictionary(sampleRate => sampleRate, sampleRate => new List<RepresentationViewModel>());

            // find sample rate
            var sampleRates = exportParameters.ResourcePaths.Select(resourcePath 
                => new SampleRateContainer(resourcePath.Split("/").Last()).ToUnitString());

            if (sampleRates.Any())
                this.SampleRate = sampleRates.First();

            //
            this.ExportParameters = exportParameters;
            var selectedRepresentations = this.GetSelectedRepresentations();

            exportParameters.ResourcePaths.ForEach(value =>
            {
                var pathSegments = value.Split('/');
                var catalogName = $"/{pathSegments[1]}/{pathSegments[2]}/{pathSegments[3]}";
                var resourceName = pathSegments[4];
                var representationName = pathSegments[5];

                var catalogContainer = this.CatalogContainersInfo.Accessible.FirstOrDefault(current => current.Id == catalogName);

                if (catalogContainer != null)
                {
                    var resources = _appState.GetResources(catalogContainer);
                    var resource = resources.FirstOrDefault(current => current.Id.ToString() == resourceName);

                    if (resource != null)
                    {
                        var representation = resource.Representations.FirstOrDefault(current => current.Name == representationName);

                        if (representation != null)
                            selectedRepresentations.Add(representation);
                    }
                }
            });

            this.RaisePropertyChanged(nameof(UserState.ExportParameters));
        }

        public bool IsRepresentationSelected(RepresentationViewModel representation)
        {
            return this.SelectedRepresentations.Contains(representation);
        }

        public void ToggleRepresentationSelection(RepresentationViewModel representation)
        {
            var isSelected = this.SelectedRepresentations.Contains(representation);

            if (isSelected)
                this.GetSelectedRepresentations().Remove(representation);
            else
                this.GetSelectedRepresentations().Add(representation);

            this.UpdateExportParameters();
            this.RaisePropertyChanged(nameof(UserState.SelectedRepresentations));
        }

        public long GetByteCount()
        {
            var totalDays = (this.DateTimeEnd - this.DateTimeBegin).TotalDays;
            var frequency = string.IsNullOrWhiteSpace(this.SampleRate) ? 0 : new SampleRateContainer(this.SampleRate).SamplesPerDay;

            return (long)this.GetSelectedRepresentations().Sum(representation =>
            {
                var elementSize = NexusCoreUtilities.SizeOf(representation.DataType);

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
                return catalogContainer.Catalog.Resources.SelectMany(resource =>
                {
                    return resource.Representations.Select(representation => representation.Id.Split('_')[0]);
                });
            }).Distinct().OrderBy(x => x, new SampleRateStringComparer()).ToList();

            // to rebuilt list with new representation instances
            this.SetExportParameters(this.ExportParameters);

            // maybe there is a new resource available now: display it
            this.UpdateGroupedResources();
        }

        private void UpdateAttachments()
        {
            this.Attachments = null;

            if (this.CatalogContainer != null)
            {
                var folderPath = Path.Combine(_pathsOptions.Attachments, this.CatalogContainer.PhysicalName);

                if (Directory.Exists(folderPath))
                    this.Attachments = Directory.GetFiles(folderPath, "*").ToList();
            }
        }

        private void UpdateGroupedResources()
        {
            if (this.CatalogContainer is not null)
            {
                this.GroupedResources = new Dictionary<string, List<ResourceViewModel>>();

                foreach (var resource in _appState.GetResources(this.CatalogContainer))
                {
                    if (this.ResourceMatchesFilter(resource))
                    {
                        var groupNames = resource.Group.Split('\n');

                        foreach (string groupName in groupNames)
                        {
                            var success = this.GroupedResources.TryGetValue(groupName, out var group);

                            if (!success)
                            {
                                group = new List<ResourceViewModel>();
                                this.GroupedResources[groupName] = group;
                            }

                            group.Add(resource);
                        }
                    }
                }

                foreach (var entry in this.GroupedResources)
                {
                    entry.Value.Sort((x, y) => x.Name.CompareTo(y.Name));
                }

                if (this.GroupedResources.Any())
                {
                    // try find previously selected group
                    if (this.GroupedResourcesEntry.Value is not null)
                        this.GroupedResourcesEntry = this.GroupedResources
                            .FirstOrDefault(entry => entry.Key == this.GroupedResourcesEntry.Key);

                    // otherwise select first group
                    if (this.GroupedResourcesEntry.Value is null)
                        this.GroupedResourcesEntry = this.GroupedResources
                            .OrderBy(entry => entry.Key)
                            .First();
                }
                else
                    this.GroupedResourcesEntry = default;
            }
            else
            {
                this.GroupedResourcesEntry = default;
            }
        }

        private void UpdateExportParameters()
        {
            this.ExportParameters.ResourcePaths = this.GetSelectedRepresentations().Select(representation =>
            {
                return $"{representation.Parent.Parent.Id}/{representation.Parent.Id}/{representation.Name}";
            }).ToList();
        }

        private bool ResourceMatchesFilter(ResourceViewModel resource)
        {
            if (string.IsNullOrWhiteSpace(this.SearchString))
                return true;

            if (resource.Name.Contains(this.SearchString, StringComparison.OrdinalIgnoreCase) 
             || resource.Description.Contains(this.SearchString, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private List<RepresentationViewModel> GetSelectedRepresentations()
        {
            var containsKey = !string.IsNullOrWhiteSpace(this.SampleRate) && _sampleRateToSelectedRepresentationsMap.ContainsKey(this.SampleRate);

            if (containsKey)
                return _sampleRateToSelectedRepresentationsMap[this.SampleRate];
            else
                return new List<RepresentationViewModel>();
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
