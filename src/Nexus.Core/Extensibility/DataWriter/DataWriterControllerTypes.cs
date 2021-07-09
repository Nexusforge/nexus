using Nexus.DataModel;
using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    public record RepresentationPipeReader(
        RepresentationRecord RepresentationRecord,
        PipeReader DataReader);
}
