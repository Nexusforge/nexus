using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;

namespace Nexus.Extensions
{
    public interface IPipeMessage
    {
        string Type { get; }
    }

    public record LogMessage(LogLevel LogLevel, string Message)
        : IPipeMessage
    {
        public string Type { get; } = nameof(LogMessage);
        public int Version { get; } = 1;
    }

    public record ProtocolRequest(string[] AvailableProtocols)
       : IPipeMessage
    {
        public string Type { get; } = nameof(ProtocolRequest);
    }

    public record ProtocolResponse(string SelectedProtocol)
       : IPipeMessage
    {
        public string Type { get; } = nameof(ProtocolResponse);
    }

    public record CatalogsRequest()
        : IPipeMessage
    {
        public string Type { get; } = nameof(CatalogsRequest);
        public int Version { get; } = 1;
    }

    public record CatalogsResponse(List<Catalog> Catalogs)
        : IPipeMessage
    {
        public string Type { get; } = nameof(CatalogsResponse);
        public int Version { get; } = 1;
    }

    public record TimeRangeRequest(string CatalogId)
       : IPipeMessage
    {
        public string Type { get; } = nameof(TimeRangeRequest);
        public int Version { get; } = 1;
    }

    public record TimeRangeResponse(DateTime Begin, DateTime End)
        : IPipeMessage
    {
        public string Type { get; } = nameof(TimeRangeResponse);
        public int Version { get; } = 1;
    }

    public record AvailabilityRequest(string CatalogId, DateTime Begin, DateTime End)
      : IPipeMessage
    {
        public string Type { get; } = nameof(AvailabilityRequest);
        public int Version { get; } = 1;
    }

    public record AvailabilityResponse(double Availability)
        : IPipeMessage
    {
        public string Type { get; } = nameof(AvailabilityResponse);
        public int Version { get; } = 1;
    }

    public record ReadSingleRequest(Dataset dataset, int Length, DateTime Begin, DateTime End)
      : IPipeMessage
    {
        public string Type { get; } = nameof(AvailabilityRequest);
        public int Version { get; } = 1;
    }

    public record ReadSingleResponse()
        : IPipeMessage
    {
        public string Type { get; } = nameof(AvailabilityResponse);
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
