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
            DatasetRecord[] datasetRecords, 
            CancellationToken cancellationToken);

        Task WriteAsync(
            TimeSpan fileOffset,
            TimeSpan samplePeriod,
            WriteRequest[] writeRequests,
            CancellationToken cancellationToken);

        Task CloseAsync(
            CancellationToken cancellationToken);
    }
}