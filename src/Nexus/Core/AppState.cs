using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.DataModel;
using Nexus.Services;
using Nexus.Utilities;
using Nexus.ViewModels;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Core
{
    public class AppState : BindableBase
    {
        #region Fields

        private bool _isDatabaseInitialized;
        private bool _isDatabaseUpdating;
        private SemaphoreSlim _updateDatabaseSemaphore;
        private CancellationTokenSource _updateDatabaseCancellationTokenSource;
        private Dictionary<CatalogContainer, List<ResourceViewModel>> _resourceCache;

        #endregion

        #region Constructors

        public AppState(ILogger<AppState> logger)
        {
            this.Logger = logger;
            
            this.CsvRowIndexFormatValues = NexusUtilities.GetEnumValues<CsvRowIndexFormat>();
            this.CodeLanguageValues = NexusUtilities.GetEnumValues<CodeLanguage>();
            this.CodeTypeValues = NexusUtilities.GetEnumValues<CodeType>();
            this.FileFormatValues = NexusUtilities.GetEnumValues<FileFormat>();
            this.FileGranularityValues = NexusUtilities.GetEnumValues<FileGranularity>();
            this.Version = Assembly.GetEntryAssembly().GetName().Version.ToString();
        }

        #endregion

        #region Properties - General

        public bool IsDatabaseInitialized
        {
            get { return _isDatabaseInitialized; }
            set { this.SetProperty(ref _isDatabaseInitialized, value); }
        }

        public bool IsDatabaseUpdating
        {
            get { return _isDatabaseUpdating; }
            set { this.SetProperty(ref _isDatabaseUpdating, value); }
        }

        public ILogger<AppState> Logger { get; }

        public string Version { get; }

        #endregion

        #region Properties - Settings

        public List<FileGranularity> FileGranularityValues { get; }

        public List<FileFormat> FileFormatValues { get; }

        public List<CsvRowIndexFormat> CsvRowIndexFormatValues { get; }

        #endregion

        #region Properties - Filter

        public List<CodeType> CodeTypeValues { get; }

        public List<CodeLanguage> CodeLanguageValues { get; }

        public FilterSettingsViewModel FilterSettings { get; set; }

        #endregion

        #region Properties - News

        public NewsPaper NewsPaper { get; set; }

        #endregion

        #region Methods

        public async Task UpdateDatabaseAsync()
        {
            _updateDatabaseCancellationTokenSource?.Cancel();
            _updateDatabaseCancellationTokenSource = new CancellationTokenSource();

            await _updateDatabaseSemaphore.WaitAsync();

            try
            { 
                this.IsDatabaseUpdating = true;
                await _databaseManager.UpdateAsync(_updateDatabaseCancellationTokenSource.Token);
                _resourceCache.Clear();
                this.IsDatabaseInitialized = true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex.GetFullMessage());
                throw;
            }
            finally
            {
                this.IsDatabaseUpdating = false;
                _updateDatabaseSemaphore.Release();
            }
        }

        public List<ResourceViewModel> GetResources(CatalogContainer catalogContainer)
        {
            if (!_resourceCache.TryGetValue(catalogContainer, out var resources))
            {
                resources = catalogContainer.Catalog.Resources.Select(resource =>
                {
                    var resourceMeta = catalogContainer.CatalogSettings.Resources.First(resourceMeta => resourceMeta.Id == resource.Id);
                    return new ResourceViewModel(resource, resourceMeta);
                }).ToList();

                _resourceCache[catalogContainer] = resources;
            }

            return resources;
        }

        #endregion
    }
}
