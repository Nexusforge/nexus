using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Extensibility;
using Nexus.Extensions;
using System;

namespace DataSource
{
    public class DataSourceControllerFixture : IDisposable
    {
        public DataSourceControllerFixture()
        {
            var dataSource = new InMemoryDataSource();

            this.BackendSource = new BackendSource(Type: "Nexus.Builtin.Inmemory", ResourceLocator: new Uri("memory://localhost"));
            this.Controller = new DataSourceController(dataSource, this.BackendSource, NullLogger.Instance);
        }

        public DataSourceController Controller { get; }

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
