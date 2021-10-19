using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Utilities;
using Nexus.ViewModels;
using Prism.Mvvm;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace Nexus.Core
{
    public class AppState : BindableBase
    {
        #region Fields

        private CatalogState _catalogState;
        private bool _isCatalogStateUpdating;

        #endregion

        #region Constructors

        public AppState(ILogger<AppState> logger)
        {
            this.Logger = logger;
            
            this.CodeLanguageValues = NexusCoreUtilities.GetEnumValues<CodeLanguage>();
            this.CodeTypeValues = NexusCoreUtilities.GetEnumValues<CodeType>();
            this.Version = Assembly.GetEntryAssembly().GetName().Version.ToString();
        }

        #endregion

        #region Properties - General

        public ConcurrentDictionary<CatalogContainer, ResourceViewModel[]> ResourceCache => new ConcurrentDictionary<CatalogContainer, ResourceViewModel[]>();

        public NexusProject Project { get; set; }

        public CatalogState CatalogState
        {
            get { return _catalogState; }
            set { this.SetProperty(ref _catalogState, value); }
        }

        public bool IsCatalogStateUpdating
        {
            get { return _isCatalogStateUpdating; }
            set { this.SetProperty(ref _isCatalogStateUpdating, value); }
        }

        public ILogger<AppState> Logger { get; }

        public string Version { get; }

        #endregion

        #region Properties - Filter

        public List<CodeType> CodeTypeValues { get; }

        public List<CodeLanguage> CodeLanguageValues { get; }

        public FilterSettingsViewModel FilterSettings { get; set; }

        #endregion

        #region Properties - News

        public NewsPaper NewsPaper { get; set; }

        #endregion
    }
}
