using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;

namespace Nexus.Extensibility
{
    public record DataWriterContext()
    {
        public Uri ResourceLocator { get; init; }
        public Dictionary<string, string> Configuration { get; init; }
        public ILogger Logger { get; init; }
    }

    public record CatalogItemGroup(
        ResourceCatalog Catalog,
        string License, 
        CatalogItem[] CatalogItems);

    public record WriteRequestGroup(
        ResourceCatalog Catalog,
        WriteRequest[] Requests);

    public record WriteRequest(
        CatalogItem CatalogItem,
        ReadOnlyMemory<double> Data);
}
