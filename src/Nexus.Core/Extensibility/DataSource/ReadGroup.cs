namespace Nexus.Extensibility
{
    public record ReadGroup(DataSourceController Controller, List<DatasetPipeWriter> DatasetPipeWriters);
}
