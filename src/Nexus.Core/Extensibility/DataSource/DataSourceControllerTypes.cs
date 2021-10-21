using Nexus.DataModel;
using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    internal record CatalogItemPipeWriter(
        CatalogItem CatalogItem, 
        PipeWriter DataWriter, 
        PipeWriter? StatusWriter);

    internal record DataReadingGroup(
        IDataSourceController Controller,
        CatalogItemPipeWriter[] CatalogItemPipeWriters);
}
