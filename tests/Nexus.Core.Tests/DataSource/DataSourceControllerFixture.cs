using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Extensibility;
using Nexus.Sources;
using System;

namespace DataSource
{
    public class DataSourceControllerFixture : IDisposable
    {
        public DataSourceControllerFixture()
        {
            var dataSource = new InMemory();

            this.BackendSource = new BackendSource(
                Type: typeof(InMemory).FullName ?? throw new Exception("full name is null"), 
                ResourceLocator: new Uri("memory://localhost"),
                Configuration: default);

            this.Controller = new DataSourceController(dataSource, this.BackendSource, NullLogger.Instance);
        }

        internal DataSourceController Controller { get; }

        public BackendSource BackendSource { get; }

        public void Dispose()
        {
            try
            {
                this.Controller.Dispose();
            }
            catch
            {
                //
            }
        }       
    }
}
