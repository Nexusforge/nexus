using Nexus.DataModel;
using System.Collections.Concurrent;
using System.Reflection;

namespace Nexus.Core
{
    internal class AppState
    {
        #region Constructors

        public AppState()
        {
            var entryAssembly = Assembly.GetEntryAssembly()!;
            var version = entryAssembly.GetName().Version!;

            Version = version.ToString();
        }

        #endregion

        #region Properties - General

        public ConcurrentDictionary<CatalogContainer, Task<Resource[]>> ResourceCache { get; } 
            = new ConcurrentDictionary<CatalogContainer, Task<Resource[]>>();

        public string Version { get; }

        public Task? ReloadPackagesTask { get; set; }

        // these properties will be set during host startup
        public NexusProject Project { get; set; } = default!;

        public CatalogState CatalogState { get; set; } = default!;

        public List<ExtensionDescription> DataWriterDescriptions { get; set; } = default!;

        #endregion
    }
}
