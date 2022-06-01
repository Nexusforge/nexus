using Nexus.Core;
using Nexus.Extensibility;
using Nexus.Sources;

namespace DataSource
{
    public class DataSourceControllerFixture
    {
        public DataSourceControllerFixture()
        {
            DataSource = new Sample();

            Registration = new DataSourceRegistration(
                Type: typeof(Sample).FullName!, 
                ResourceLocator: new Uri("memory://localhost"),
                Configuration: new Dictionary<string, string>(),
                Publish: true);

            RequestConfiguration = new Dictionary<string, string>();
        }

        internal IDataSource DataSource { get; }
        internal DataSourceRegistration Registration { get; }
        internal Dictionary<string, string> RequestConfiguration { get; }  
    }
}
