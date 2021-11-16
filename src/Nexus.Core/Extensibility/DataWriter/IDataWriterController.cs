using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    internal interface IDataWriterController : IDisposable
    {
        Task InitializeAsync(ILogger logger, CancellationToken cancellationToken);
        Task WriteAsync(DateTime begin, DateTime end, TimeSpan samplePeriod, TimeSpan filePeriod, CatalogItemPipeReader[] catalogItemPipeReaders, IProgress<double> progress, CancellationToken cancellationToken);
    }
}