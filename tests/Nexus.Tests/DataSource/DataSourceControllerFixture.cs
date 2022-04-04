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
            var dataSource = new Sample();

            var registration = new DataSourceRegistration(
                Type: typeof(Sample).FullName!, 
                ResourceLocator: new Uri("memory://localhost"),
                Configuration: new Dictionary<string, string>(),
                Publish: true);

            var userConfiguration = new Dictionary<string, string>();

            Controller = new DataSourceController(
                dataSource,
                registration,
                userConfiguration,
                default!,
                NullLogger<DataSourceController>.Instance);
        }

        internal DataSourceController Controller { get; }

        public void Dispose()
        {
            try
            {
                Controller.Dispose();
            }
            catch
            {
                //
            }
        }       
    }
}
