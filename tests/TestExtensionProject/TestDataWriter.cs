using Nexus.DataModel;
using Nexus.Extensibility;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TestExtensionProject
{
    [ExtensionIdentification("my-unique-data-writer", "My unique data writer", "A data writer for unit tests.")]
    public class TestDataWriter : IDataWriter
    {
        public Task CloseAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException(nameof(CloseAsync));
        }

        public Task OpenAsync(DateTime fileBegin, TimeSpan samplePeriod, CatalogItem[] catalogItems, CancellationToken cancellationToken)
        {
            throw new NotImplementedException(nameof(OpenAsync));
        }

        public Task SetContextAsync(DataWriterContext context, CancellationToken cancellationToken)
        {
            throw new NotImplementedException(nameof(SetContextAsync));
        }

        public Task WriteAsync(TimeSpan fileOffset, WriteRequest[] requests, IProgress<double> progress, CancellationToken cancellationToken)
        {
            throw new NotImplementedException(nameof(WriteAsync));
        }
    }
}
