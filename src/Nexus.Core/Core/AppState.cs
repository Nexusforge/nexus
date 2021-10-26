using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Utilities;
using Nexus.ViewModels;
using Prism.Mvvm;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace Nexus.Core
{
    internal class AppState : BindableBase
    {
        #region Fields

        private CatalogState _catalogState;
        private bool _isCatalogStateUpdating;

        #endregion

        #region Constructors

        public AppState()
        {
            this.CodeLanguageValues = NexusCoreUtilities.GetEnumValues<CodeLanguage>();
            this.CodeTypeValues = NexusCoreUtilities.GetEnumValues<CodeType>();
            this.Version = Assembly.GetEntryAssembly().GetName().Version.ToString();
        }

        #endregion

        #region Properties - General

        public ConcurrentDictionary<CatalogContainer, ResourceViewModel[]> ResourceCache { get; } = new ConcurrentDictionary<CatalogContainer, ResourceViewModel[]>();

        public NexusProject Project { get; set; }

        public CatalogState? CatalogState
        {
            get { return _catalogState; }
            set { this.SetProperty(ref _catalogState, value); }
        }

        public bool IsCatalogStateUpdating
        {
            get { return _isCatalogStateUpdating; }
            set { this.SetProperty(ref _isCatalogStateUpdating, value); }
        }

        public string Version { get; }

        public Dictionary<string, (string FormatName, OptionAttrbute[] Options)> DataWriterInfoMap { get; set; }

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
