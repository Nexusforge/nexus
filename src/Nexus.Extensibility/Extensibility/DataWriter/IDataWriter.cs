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
            RepresentationRecordGroup[] representationRecordGroups, 
            CancellationToken cancellationToken);

        Task WriteAsync(
            TimeSpan fileOffset,
            TimeSpan samplePeriod,
            WriteRequestGroup[] requestGroups,
            Progress<double> progress,
            CancellationToken cancellationToken);

        Task CloseAsync(
            CancellationToken cancellationToken);
    }
}