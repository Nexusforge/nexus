using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Extensibility;
using Nexus.Models;
using Nexus.Sources;
using System;

namespace DataSource
{
    public class DataSourceControllerFixture : IDisposable
    {
        public DataSourceControllerFixture()
        {
            var dataSource = new InMemory();

            var backendSource = new BackendSource(
                Type: typeof(InMemory).FullName ?? throw new Exception("full name is null"), 
                ResourceLocator: new Uri("memory://localhost"),
                Configuration: default,
                Publish: true);

            this.Controller = new DataSourceController(
                dataSource,
                backendSource,
                NullLogger<DataSourceController>.Instance);
        }

        internal DataSourceController Controller { get; }

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
