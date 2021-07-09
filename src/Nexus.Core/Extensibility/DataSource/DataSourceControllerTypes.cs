﻿using Nexus.DataModel;
using System.Collections.Generic;
using System.IO.Pipelines;

namespace Nexus.Extensibility
{
    public record CatalogItemPipeWriter(
        CatalogItem CatalogItem, 
        PipeWriter DataWriter, 
        PipeWriter? StatusWriter);

    public record DataReadingGroup(
        DataSourceController Controller,
        List<CatalogItemPipeWriter> CatalogItemPipeWriters);
}
