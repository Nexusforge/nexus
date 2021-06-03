using Nexus.DataModel;
using System;
using System.Collections.Generic;

namespace Nexus.Extensions
{
    public interface IPipeMessage
    {
        string Type { get; }
    }

    public record CatalogsRequest()
        : IPipeMessage
    {
        public string Type { get; } = nameof(CatalogsRequest);
        public int Version { get; } = 1;
    }

    public record CatalogResponse(List<Catalog> Catalogs)
        : IPipeMessage
    {
        public string Type { get; } = nameof(CatalogResponse);
        public int Version { get; } = 1;
    }

    public record ShutdownRequest()
        : IPipeMessage
    {
        public string Type { get; } = nameof(ShutdownRequest);
        public int Version { get; } = 1;
    }

    public class PipeProtocolException : Exception
    {
        public PipeProtocolException(string message, Exception innerException = null)
            : base(message, innerException)
        {
            //
        }
    }
}
