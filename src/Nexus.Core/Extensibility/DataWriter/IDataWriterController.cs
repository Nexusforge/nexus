using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public interface IDataWriterController : IDisposable
    {
        Task InitializeAsync(CancellationToken cancellationToken);
        Task WriteAsync(DateTime begin, DateTime end, TimeSpan samplePeriod, TimeSpan filePeriod, CatalogItemPipeReader[] catalogItemPipeReaders, IProgress<double> progress, CancellationToken cancellationToken);
    }
}