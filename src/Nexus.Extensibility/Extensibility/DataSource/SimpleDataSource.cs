using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public abstract class SimpleDataSource : IDataSource
    {
        #region Fields

        private List<Project> _projects;

        #endregion

        #region Properties

        public string RootPath { get; private set; }

        public ILogger Logger { get; private set; }

        public IReadOnlyList<Project> Projects => _projects;

        public Dictionary<string, string> Options { get; private set; }

        #endregion

        #region Methods

        public Project GetProject(string projectId)
        {
            return this.Projects.First(project => project.Id == projectId);
        }

        public abstract Task<List<Project>> InitializeAsync();

        #endregion

        #region IDataSource

        public async Task<List<Project>> InitializeAsync(string rootPath, ILogger logger, Dictionary<string, string> options)
        {
            this.RootPath = rootPath;
            this.Logger = logger;
            this.Options = options;

            _projects = await this.InitializeAsync();

            return _projects;
        }

        public abstract Task<double> GetAvailabilityAsync(string projectId, DateTime day);

        public abstract Task<ReadResult<T>> ReadSingleAsync<T>(Dataset dataset, DateTime begin, DateTime end) 
            where T : unmanaged;

        #endregion

        #region IDisposable

        private bool disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    //
                }

                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
