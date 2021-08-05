using Nexus.DataModel;
using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    public record CatalogItemPipeWriter(
        CatalogItem CatalogItem, 
        PipeWriter DataWriter, 
        PipeWriter? StatusWriter);

    public record DataReadingGroup(
        IDataSourceController Controller,
        CatalogItemPipeWriter[] CatalogItemPipeWriters);
}
