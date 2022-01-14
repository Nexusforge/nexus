using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Extensibility;
using Nexus.Models;
using Nexus.Sources;
using System;
using System.Collections.Generic;

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
                Configuration: new Dictionary<string, string>(),
                Publish: true);

            var userConfiguration = new Dictionary<string, string>();

            this.Controller = new DataSourceController(
                dataSource,
                backendSource,
                userConfiguration,
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
