using Nexus.DataModel;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensibility
{
    public interface IDataWriter
    {
        Task SetContextAsync(
            DataWriterContext context, 
            CancellationToken cancellationToken);

        Task OpenAsync(
            DateTime fileBegin, 
            TimeSpan samplePeriod,
            CatalogItem[] catalogItems, 
            CancellationToken cancellationToken);

        Task WriteAsync(
            TimeSpan fileOffset,
            WriteRequest[] requests,
            IProgress<double> progress,
            CancellationToken cancellationToken);

        Task CloseAsync(
            CancellationToken cancellationToken);
    }
}