using Nexus.DataModel;
using System.Collections.Generic;
using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    public record DatasetRecordPipe(DatasetRecord DatasetRecord, PipeWriter DataWriter, PipeWriter? StatusWriter);

    public record DataSourceReadingGroup(DataSourceController Controller, List<DatasetRecordPipe> DatasetRecordPipes);
}
