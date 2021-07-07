using Nexus.DataModel;
using System.Collections.Generic;
using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    public record DatasetPipeWriter(
        DatasetRecord DatasetRecord, 
        PipeWriter DataWriter, 
        PipeWriter? StatusWriter);

    public record DataReadingGroup(
        DataSourceController Controller,
        List<DatasetPipeWriter> DatasetPipeWriters);
}
