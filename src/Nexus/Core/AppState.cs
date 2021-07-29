using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.DataModel;
using Nexus.Infrastructure;
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

        private UserManager _userManager;
        private bool _isDatabaseInitialized;
        private bool _isDatabaseUpdating;
        private SemaphoreSlim _updateDatabaseSemaphore;
        private CancellationTokenSource _updateDatabaseCancellationTokenSource;
        private IDatabaseManager _databaseManager;
        private Dictionary<CatalogContainer, List<ResourceViewModel>> _resourceCache;

        #endregion

        #region Constructors

        public AppState(ILogger<AppState> logger,
                        IDatabaseManager databaseManager,
                        UserManager userManager,
                        ExtensionHive extensionHive,
                        IOptions<PathsOptions> pathsOptions)
        {
            this.Logger = logger;
            _databaseManager = databaseManager;
            _userManager = userManager;

            await extensionHive.LoadPackagesAsync(packageReferences, cancellationToken);

            this.CsvRowIndexFormatValues = NexusUtilities.GetEnumValues<CsvRowIndexFormat>();
            this.CodeLanguageValues = NexusUtilities.GetEnumValues<CodeLanguage>();
            this.CodeTypeValues = NexusUtilities.GetEnumValues<CodeType>();
            this.FileFormatValues = NexusUtilities.GetEnumValues<FileFormat>();
            this.FileGranularityValues = NexusUtilities.GetEnumValues<FileGranularity>();
            this.Version = Assembly.GetEntryAssembly().GetName().Version.ToString();

            this.InitializeApp(pathsOptions.Value);
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

        public FilterSettingsViewModel FilterSettings { get; private set; }

        #endregion

        #region Properties - News

        public NewsPaper NewsPaper { get; private set; }

        #endregion

        #region Methods

        public void InitializeApp(PathsOptions pathOptions)
        {
            try
            {
                Directory.CreateDirectory(pathOptions.Data);

                this.NewsPaper = NewsPaper.Load(Path.Combine(pathOptions.Data, "news.json"));

                var filterSettingsFilePath = Path.Combine(pathOptions.Data, "filters.json");
                this.FilterSettings = new FilterSettingsViewModel(filterSettingsFilePath);
                this.InitializeFilterSettings(this.FilterSettings.Model, filterSettingsFilePath);

                _resourceCache = new Dictionary<CatalogContainer, List<ResourceViewModel>>();
                _updateDatabaseSemaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);
                _userManager.Initialize();

                _ = this.UpdateDatabaseAsync();
            }
            catch (Exception ex)
            {
                this.Logger.LogError(ex.GetFullMessage());
                throw;
            }
        }

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

        private void InitializeFilterSettings(FilterSettings filterSettings, string filePath)
        {
            // ensure that code samples of test user are present
            var testCodes = filterSettings.CodeDefinitions.Where(code => code.Owner == "test@nexus.org");

            if (!testCodes.Any(testCode => testCode.Name == "Simple filter (C#)"))
            {
                using var streamReader1 = new StreamReader(ResourceLoader.GetResourceStream("Nexus.Resources.TestUserFilterCodeTemplateSimple.cs"));

                filterSettings.CodeDefinitions.Add(new CodeDefinition()
                {
                    Code = streamReader1.ReadToEnd(),
                    CodeLanguage = CodeLanguage.CSharp,
                    CodeType = CodeType.Filter,
                    CreationDate = DateTime.UtcNow,
                    IsEnabled = true,
                    Name = "Simple filter (C#)",
                    Owner = "test@nexus.org",
                    RequestedCatalogIds = new List<string>() { "/IN_MEMORY/TEST/ACCESSIBLE" },
                    SampleRate = "1 s"
                });

                filterSettings.Save(filePath);
            }

            if (!testCodes.Any(testCode => testCode.Name == "Simple shared (C#)"))
            {
                using var streamReader2 = new StreamReader(ResourceLoader.GetResourceStream("Nexus.Resources.TestUserSharedCodeTemplateSimple.cs"));

                filterSettings.CodeDefinitions.Add(new CodeDefinition()
                {
                    Code = streamReader2.ReadToEnd(),
                    CodeLanguage = CodeLanguage.CSharp,
                    CodeType = CodeType.Shared,
                    CreationDate = DateTime.UtcNow,
                    IsEnabled = true,
                    Name = "Simple shared (C#)",
                    Owner = "test@nexus.org",
                    RequestedCatalogIds = new List<string>(),
                    SampleRate = string.Empty
                });

                filterSettings.Save(filePath);
            }
        }

        #endregion
    }
}
