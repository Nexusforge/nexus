using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Extensibility;
using Nexus.Extensions;
using System;

namespace Nexus.Core.Tests
{
    public class DataSourceControllerFixture : IDisposable
    {
        public DataSourceControllerFixture()
        {
            var dataSource = new InMemoryDataSource();

            var backendSource = new BackendSource()
            {
                Type = "Nexus.InMemory",
                ResourceLocator = new Uri("memory://localhost")
            };

            this.Controller = new DataSourceController(dataSource, backendSource, NullLogger.Instance);
        }

        public DataSourceController Controller { get; }

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
