using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Nexus.DataModel;
using Nexus.Services;
using Nexus.Utilities;
using Nexus.ViewModels;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Core
{
    internal class UserState : BindableBase, IDisposable
    {
        #region Fields

        private DateTime _dateTimeBeginWorkaround;
        private DateTime _dateTimeEndWorkaround;

        private string _searchString;
        private TimeSpan _samplePeriod;

        private double _readProgress;
        private double _writeProgress;
        private double _visualizeProgress;

        private bool _isEditEnabled;
        private bool _visualizeBeginAtZero;

        private ClientState _clientState;
        private IDatabaseManager _databaseManager;
        private IJSRuntime _jsRuntime;
        private IUserIdService _userIdService;
        private IServiceProvider _serviceProvider;

        private AppState _appState;
        private AuthenticationStateProvider _authenticationStateProvider;
        private CatalogContainer _catalogContainer;
        private DataService _dataService;
        private ExportParameters _exportParameters;
        private JobControl<ExportJob> _exportJobControl;

        private KeyValuePair<string, List<ResourceViewModel>> _groupedResourcesEntry;
        private Dictionary<TimeSpan, List<RepresentationViewModel>> _samplePeriodToSelectedRepresentationsMap = new Dictionary<TimeSpan, List<RepresentationViewModel>>();

        #endregion

        #region Constructors

        public UserState(ILogger<UserState> logger,
                         IJSRuntime jsRuntime,
                         IDatabaseManager databaseManager,
                         IUserIdService userIdService,
                         IServiceProvider serviceProvider,
                         AppState appState,
                         AuthenticationStateProvider authenticationStateProvider,
                         DataService dataService)
        {
            this.Logger = logger;

            _jsRuntime = jsRuntime;
            _databaseManager = databaseManager;
            _userIdService = userIdService;
            _serviceProvider = serviceProvider;
            _appState = appState;
            _authenticationStateProvider = authenticationStateProvider;
            _dataService = dataService;

            this.VisualizeBeginAtZero = true;
            this.ExportParameters = new ExportParameters();

            _appState.PropertyChanged += this.OnAppStatePropertyChanged;

            if (_appState.CatalogState is not null)
                this.InitializeAsync(_appState.CatalogState.CatalogContainers, CancellationToken.None).Wait();
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
                    foreach (var catalogContainer in _appState.CatalogState.CatalogContainers)
                    {
                        //_databaseManager.SaveCatalogMeta(catalogContainer.CatalogMetadata);
                    }
                }

                this.SetProperty(ref _isEditEnabled, value);
            }
        }

        public ClientState ClientState
        {
            get { return _clientState; }
            set { this.SetProperty(ref _clientState, value); }
        }

        public double ReadProgress
        {
            get { return _readProgress; }
            set { this.SetProperty(ref _readProgress, value); }
        }

        public double WriteProgress
        {
            get { return _writeProgress; }
            set { this.SetProperty(ref _writeProgress, value); }
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
                _ = Task.Run(async () =>
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
                _ = Task.Run(async () =>
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

        public bool VisualizeBeginAtZero
        {
            get { return _visualizeBeginAtZero; }
            set { this.SetProperty(ref _visualizeBeginAtZero, value); }
        }

        public TimeSpan SamplePeriod
        {
            get
            {
                return _samplePeriod;
            }
            set
            {
                this.SetProperty(ref _samplePeriod, value);
                this.UpdateExportParameters();
            }
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
                // When database is updated and then the selected catalog is changed,
                // "value" refers to an old catalog container that does not exist in 
                // the database anymore.
                if (value is not null && !_appState.CatalogState.CatalogContainers.Contains(value))
                    value = _appState.CatalogState.CatalogContainers.FirstOrDefault(container => container.Id == value.Id);

                this.SetProperty(ref _catalogContainer, value);

                _searchString = string.Empty;

                if (this.CatalogContainersInfo.Accessible.Contains(value))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await this.UpdateGroupedResourcesAsync(CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            this.Logger.LogWarning(ex, "Unable to update grouped resources");
                        }
                    });
                    
                    this.UpdateAttachments();
                }
                else
                {
                    this.GroupedResources = null;
                    this.Attachments = null;
                }
            }
        }

        public IEnumerable<string> Attachments { get; private set; }

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
                _ = this.UpdateGroupedResourcesAsync(CancellationToken.None);
            }
        }

        #endregion

        #region Properties - Download Area

        public IReadOnlyCollection<RepresentationViewModel> SelectedRepresentations => this.GetSelectedRepresentations();

        #endregion

        #region Properties - Visualization

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

        public TimeSpan FilePeriod
        {
            get { return this.ExportParameters.FilePeriod; }
            set 
            {
                this.ExportParameters.FilePeriod = value;
                this.RaisePropertyChanged();
            }
        }

        public string Writer
        {
            get { return this.ExportParameters.Type; }
            set
            {
                this.ExportParameters.Type = value;
                this.RaisePropertyChanged();
            }
        }

        public Dictionary<string, string> Configuration
        {
            get
            {
                return this.ExportParameters.Configuration;
            }
        }

        #endregion

        #region Methods

        public bool CanDownload()
        {
            if (this.SamplePeriod != default)
            {
                return this.DateTimeBegin < this.DateTimeEnd &&
                       this.SelectedRepresentations.Count > 0 &&
                       this.FilePeriod.Ticks % this.SamplePeriod.Ticks == 0;
            }
            else
            {
                return false;
            }
        }

        public async Task DownloadAsync()
        {
            EventHandler<double> readProgressEventHandler = (sender, e) =>
            {
                this.ReadProgress = e;
            };

            EventHandler<double> writeProgressEventHandler = (sender, e) =>
            {
                this.WriteProgress = e;
            };

            try
            {
                this.ClientState = ClientState.PrepareDownload;
                _dataService.ReadProgress.ProgressChanged += readProgressEventHandler;
                _dataService.WriteProgress.ProgressChanged += writeProgressEventHandler;

                var selectedRepresentations = this.GetSelectedRepresentations();

                // authorization
                var catalogIds = selectedRepresentations.Select(representation => representation.Resource.Catalog.Id).Distinct();

                foreach (var catalogId in catalogIds)
                {
                    var catalogContainer = _appState.CatalogState.CatalogContainers
                        .First(container => container.Id == catalogId);

                    if (!AuthorizationUtilities.IsCatalogAccessible(catalogContainer.Id, catalogContainer.CatalogMetadata, _userIdService.User))
                        throw new UnauthorizedAccessException($"The current user is not authorized to access catalog {catalogId}.");
                }

                //
                var job = new ExportJob(
                    Parameters: this.ExportParameters)
                {
                    Owner = _userIdService.User.Identity.Name
                };

                var exportJobService = _serviceProvider.GetRequiredService<JobService<ExportJob>>();

                _exportJobControl = exportJobService.AddJob(job, _dataService.ReadProgress, (jobControl, cts) =>
                {
                    var task = _dataService.ExportAsync(
                        this.ExportParameters,
                        selectedRepresentations.Select(current => new CatalogItem(current.Resource.Catalog.Model, current.Resource.Model, current.Model)),
                        Guid.NewGuid(),
                        cts.Token);

                    return task;
                });

                try
                {
                    var fileName = await _exportJobControl.Task;
                    await _jsRuntime.FileSaveAs(fileName, $"export/{fileName}");
                }
                catch (OperationCanceledException)
                {
                    //
                }
            }
            finally
            {
                _dataService.ReadProgress.ProgressChanged -= readProgressEventHandler;
                _dataService.WriteProgress.ProgressChanged -= writeProgressEventHandler;

                this.ClientState = ClientState.Normal;

                this.ReadProgress = 0;
                this.WriteProgress = 0;
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
            if (!AuthorizationUtilities.IsCatalogAccessible(this.CatalogContainer.Id, this.CatalogContainer.CatalogMetadata, _userIdService.User))
                throw new UnauthorizedAccessException($"The current user is not authorized to access catalog '{this.CatalogContainer.Id}'.");

            return await _dataService.GetAvailabilityAsync(this.CatalogContainer.Id, this.DateTimeBegin, this.DateTimeEnd, granularity, cancellationToken);
        }

        public async Task SetExportParametersAsync(ExportParameters exportParameters, CancellationToken cancellationToken)
        {
            _samplePeriodToSelectedRepresentationsMap.Clear();

            // find catalog items
            var representations = new List<RepresentationViewModel>();

            foreach (var resourcePath in exportParameters.ResourcePaths)
            {
                // if resource path exists
                var catalogItem = await _appState.CatalogState.CatalogContainers.TryFindAsync(resourcePath, cancellationToken);

                if (catalogItem is not null)
                {
                    // if catalog is accessible
                    var catalogContainer = this.CatalogContainersInfo.Accessible
                        .FirstOrDefault(catalogContainer => catalogContainer.Id == catalogItem.Catalog.Id);

                    if (catalogContainer is not null)
                    {
                        var resources = await _appState.ResourceCache.GetOrAdd(catalogContainer, async catalogContainer =>
                        {
                            var catalogInfo = await catalogContainer.GetCatalogInfoAsync(cancellationToken);
                            var catalogViewModel = new ResourceCatalogViewModel(catalogInfo.Catalog);

                            return catalogInfo.Catalog.Resources
                               .Select(resource => new ResourceViewModel(catalogViewModel, resource))
                               .ToArray();
                        });

                        var resource = resources.FirstOrDefault(resource => resource.Id == catalogItem.Resource.Id);

                        if (resource is not null)
                        {
                            var representation = resource.Representations.FirstOrDefault(representation => representation.Id == catalogItem.Representation.Id);

                            if (representation is not null)
                                representations.Add(representation);
                        }
                    }
                }
            }

            // find and set sample period
            var samplePeriods = representations.Select(representation => representation.SamplePeriod);

            if (samplePeriods.Any())
                this.SamplePeriod = samplePeriods.First();

            // set exportParameters
            this.ExportParameters = exportParameters;

            // fill selected representations list
            var selectedRepresentations = this.GetSelectedRepresentations();

            selectedRepresentations.AddRange(representations);

            // trigger re-render
            this.RaisePropertyChanged(nameof(UserState.ExportParameters));
        }

        public bool IsRepresentationSelected(RepresentationViewModel representation)
        {
            return this.SelectedRepresentations.Contains(representation);
        }

        public void ToggleRepresentationSelection(RepresentationViewModel representation)
        {
            var selectedRepresentations = this.GetSelectedRepresentations();
            var isSelected = this.SelectedRepresentations.Contains(representation);

            if (isSelected)
                selectedRepresentations.Remove(representation);

            else
                selectedRepresentations.Add(representation);

            this.UpdateExportParameters();
            this.RaisePropertyChanged(nameof(UserState.SelectedRepresentations));
        }

        public long GetByteCount()
        {
            var sampleCount = this.SamplePeriod.Ticks == default
                ? 0 
                : (this.DateTimeEnd - this.DateTimeBegin).Ticks / this.SamplePeriod.Ticks;

            return this.GetSelectedRepresentations().Sum(representation =>
            {
                var elementSize = NexusCoreUtilities.SizeOf(representation.DataType);
                return sampleCount * elementSize;
            });
        }

        private async Task InitializeAsync(CatalogContainer[] catalogContainers, CancellationToken cancellationToken)
        {
            this.CatalogContainersInfo = await this.SplitCampaignContainersAsync(catalogContainers);

            // this triggers a search to find the new container instance
            this.CatalogContainer = this.CatalogContainer;

            // to rebuilt list with new representation instances
            await this.SetExportParametersAsync(this.ExportParameters, cancellationToken);

            // maybe there is a new resource available now: display it
            await this.UpdateGroupedResourcesAsync(cancellationToken);
        }

        private void UpdateAttachments()
        {
            if (this.CatalogContainer is not null)
                this.Attachments = _databaseManager.EnumerateAttachements(this.CatalogContainer.Id).ToArray();

            else
                this.Attachments = Enumerable.Empty<string>();
        }

        private async Task UpdateGroupedResourcesAsync(CancellationToken cancellationToken)
        {
            if (this.CatalogContainer is not null)
            {
                this.GroupedResources = new Dictionary<string, List<ResourceViewModel>>();

                var resources = await _appState.ResourceCache.GetOrAdd(this.CatalogContainer, async catalogContainer =>
                {
                    var catalogInfo = await catalogContainer.GetCatalogInfoAsync(cancellationToken);
                    var catalogViewModel = new ResourceCatalogViewModel(catalogInfo.Catalog);

                    return catalogInfo.Catalog.Resources
                       .Select(resource => new ResourceViewModel(catalogViewModel, resource))
                       .ToArray();
                });

                foreach (var resource in resources)
                {
                    if (this.ResourceMatchesFilter(resource))
                    {
                        var groupNames = resource.Groups;

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
                    entry.Value.Sort((x, y) => x.Id.CompareTo(y.Id));
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
            this.ExportParameters = this.ExportParameters with
            {
                ResourcePaths = this
                    .GetSelectedRepresentations()
                    .Select(representation => representation.GetPath())
                    .ToArray()
            };
        }

        private bool ResourceMatchesFilter(ResourceViewModel resource)
        {
            if (string.IsNullOrWhiteSpace(this.SearchString))
                return true;

            if (resource.Id.Contains(this.SearchString, StringComparison.OrdinalIgnoreCase) ||
                resource.Description.Contains(this.SearchString, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private List<RepresentationViewModel> GetSelectedRepresentations()
        {
            if (!_samplePeriodToSelectedRepresentationsMap.TryGetValue(this.SamplePeriod, out var representations))
            {
                representations = new List<RepresentationViewModel>();
                _samplePeriodToSelectedRepresentationsMap[this.SamplePeriod] = representations;
            }

            return representations;
        }

        private async Task<SplittedCatalogContainers> SplitCampaignContainersAsync(CatalogContainer[] catalogContainers)
        {
            var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;

            // all accessible catalogs are "accessible"
            var accessible = catalogContainers
                .Where(catalogContainer => AuthorizationUtilities.IsCatalogAccessible(catalogContainer.Id, catalogContainer.CatalogMetadata, user))
                .OrderBy(catalogContainer => catalogContainer.Id).ToList();

            // all other catalogs except hidden ones are "restricted"
            var restricted = catalogContainers.Where(catalogContainer =>
            {
                var isCatalogVisible = AuthorizationUtilities.IsCatalogVisible(user, catalogContainer.CatalogMetadata);

                return !accessible.Contains(catalogContainer) && isCatalogVisible;
            }).OrderBy(catalogContainer => catalogContainer.Id).ToList();

            return new SplittedCatalogContainers(accessible, restricted);
        }

        #endregion

        #region Callbacks

        public void OnAppStatePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppState.CatalogState) ||
               (e.PropertyName == nameof(AppState.IsCatalogStateUpdating) && !_appState.IsCatalogStateUpdating))
                _ = this.InitializeAsync(_appState.CatalogState.CatalogContainers, CancellationToken.None);
        }

        #endregion

        #region Types

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
