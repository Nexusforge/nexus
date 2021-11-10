using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Extensibility;
using Nexus.Extensions;
using System;
using System.Collections.Generic;

namespace DataSource
{
    public class DataSourceControllerFixture : IDisposable
    {
        public DataSourceControllerFixture()
        {
            var dataSource = new InMemoryDataSource();

            this.BackendSource = new BackendSource(
                Type: InMemoryDataSource.Id, 
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
