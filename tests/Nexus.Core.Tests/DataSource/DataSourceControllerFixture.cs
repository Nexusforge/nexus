using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Sources;

namespace DataSource
{
    public class DataSourceControllerFixture : IDisposable
    {
        public DataSourceControllerFixture()
        {
            var dataSource = new InMemory();

            var backendSource = new BackendSource(
                Type: typeof(InMemory).FullName!, 
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
