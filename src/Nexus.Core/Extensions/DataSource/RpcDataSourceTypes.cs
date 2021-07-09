using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Extensions
{
    public record ApiLevelResponse(int ApiLevel);
    public record CatalogsResponse(List<ResourceCatalog> Catalogs);
    public record TimeRangeResponse(DateTime Begin, DateTime End);
    public record AvailabilityResponse(double Availability);
    public record ReadSingleResponse();
    public record LogMessage(LogLevel LogLevel, string Message);

    // Based on the SignalR protocol: https://github.com/aspnet/SignalR/blob/master/specs/HubProtocol.md
    public record HandshakeRequest(
        [property: JsonPropertyName("protocol")] string Protocol = "json",
        [property: JsonPropertyName("version")] int Version = 1
    );

    public record HandshakeResponse(
        [property: JsonPropertyName("error")] string? Error
    );

    public record Invocation(
        [property: JsonPropertyName("invocationId")] string? InvocationId,
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("arguments")] object[] Arguments
    )
    {
        [JsonPropertyName("type")]
        public int Type { get; } = 1;
    };

    public record Completion<T>(
       [property: JsonPropertyName("invocationId")] string? InvocationId,
       [property: JsonPropertyName("result")] T? Result,
       [property: JsonPropertyName("error")] string? Error
    )
    {
        [JsonPropertyName("type")]
        public int Type { get; } = 3;
    };

    public record Close(
        [property: JsonPropertyName("error")] string? Error
    )
    {
        [JsonPropertyName("type")]
        public int Type { get; } = 7;
    };

    public class RpcException : Exception
    {
        public RpcException(string message, Exception innerException = null)
            : base(message, innerException)
        {
            //
        }
    }

    class TimeSpanConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TimeSpan.Parse(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
