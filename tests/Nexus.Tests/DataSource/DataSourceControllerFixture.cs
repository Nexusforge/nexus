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
                Id: Guid.NewGuid(),
                Type: typeof(Sample).FullName!, 
                ResourceLocator: new Uri("memory://localhost"),
                Configuration: default);
        }

        internal IDataSource DataSource { get; }

        internal DataSourceRegistration Registration { get; } 
    }
}
