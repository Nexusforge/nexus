using Nexus.DataModel;
using Nexus.Extensibility;
using Nexus.Models;
using Nexus.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace Nexus.Core
{
    internal class AppState
    {
        #region Constructors

        public AppState()
        {
            var entryAssembly = Assembly.GetEntryAssembly() ?? throw new Exception("entry assembly is null");
            var version = entryAssembly.GetName().Version ?? throw new Exception("version is null");

            this.Version = version.ToString();
        }

        #endregion

        #region Properties - General

        public ConcurrentDictionary<CatalogContainer, Task<Resource[]>> ResourceCache { get; } 
            = new ConcurrentDictionary<CatalogContainer, Task<Resource[]>>();

        public string Version { get; }

        public Task? ReloadPackagesTask { get; set; }

        // these properties will be set during host startup
        public NexusProject Project { get; set; } = null!;

        public CatalogState CatalogState { get; set; } = null!;

        public Dictionary<string, (string FormatName, OptionAttribute[] Options)> DataWriterInfoMap { get; set; } = null!;

        #endregion
    }
}
