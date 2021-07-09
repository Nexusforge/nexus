using Nexus.DataModel;
using System.Collections.Generic;
using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    public record RepresentationPipeWriter(
        RepresentationRecord RepresentationRecord, 
        PipeWriter DataWriter, 
        PipeWriter? StatusWriter);

    public record DataReadingGroup(
        DataSourceController Controller,
        List<RepresentationPipeWriter> RepresentationPipeWriters);
}
