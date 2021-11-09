using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Services;
using Nexus.ViewModels;
using Prism.Mvvm;
using Serilog.Core;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

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
            this.Version = Assembly.GetEntryAssembly().GetName().Version.ToString();
        }

        #endregion

        #region Properties - General

        public LoggingLevelSwitch MinimumLoglevelSwitch { get; set; }

        public ConcurrentDictionary<CatalogContainer, Task<ResourceViewModel[]>> ResourceCache { get; } = new ConcurrentDictionary<CatalogContainer, Task<ResourceViewModel[]>>();

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

        public Dictionary<string, (string FormatName, OptionAttribute[] Options)> DataWriterInfoMap { get; set; }

        #endregion

        #region Properties - News

        public NewsPaper NewsPaper { get; set; }

        #endregion
    }
}