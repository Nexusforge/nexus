using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public abstract class SimpleDataSource : IDataSource
    {
        #region Properties

        public string RootPath { get; private set; }

        public ILogger Logger { get; private set; }

        public Dictionary<string, string> Options { get; private set; }

        #endregion

        #region Methods

        public abstract Task<List<Project>> InitializeAsync();

        #endregion

        #region IDataSource

        public Task<List<Project>> InitializeAsync(string rootPath, ILogger logger, Dictionary<string, string> options)
        {
            this.RootPath = rootPath;
            this.Logger = logger;
            this.Options = options;

            return this.InitializeAsync();
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
