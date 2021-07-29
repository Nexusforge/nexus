using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TestExtensionProject
{
    [ExtensionIdentification("my-unique-data-source", "My unique data source", "A data source for unit tests.")]
    public class TestDataSource : IDataSource
    {
        public Task SetContextAsync(DataSourceContext context, CancellationToken cancellationToken)
        {
            throw new NotImplementedException(nameof(SetContextAsync));
        }

        public Task<ResourceCatalog[]> GetCatalogsAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException(nameof(GetCatalogsAsync));
        }

        public Task<(DateTime Begin, DateTime End)> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException(nameof(GetTimeRangeAsync));
        }

        public Task<double> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken)
        {
            throw new NotImplementedException(nameof(GetAvailabilityAsync));
        }

        public Task ReadAsync(DateTime begin, DateTime end, ReadRequest[] requests, IProgress<double> progress, CancellationToken cancellationToken)
        {
            throw new NotImplementedException(nameof(ReadAsync));
        }
    }
}
