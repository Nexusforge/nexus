using Nexus.DataModel;
using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    public record DatasetPipeReader(
        DatasetRecord DatasetRecord,
        PipeReader DataReader);
}
