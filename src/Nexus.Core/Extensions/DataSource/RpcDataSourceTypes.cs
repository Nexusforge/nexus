using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Extensions
{
    public interface IJsonRpcServer
    {
        public Task<ApiLevelResponse> 
            GetApiLevelAsync(CancellationToken cancellationToken);

        public Task 
            SetContextAsync(string resourceLocator, Dictionary<string, string> configuration, ResourceCatalog[] catalogs, CancellationToken cancellationToken);

        public Task<CatalogsResponse>
            GetCatalogsAsync(CancellationToken cancellationToken);

        public Task<TimeRangeResponse>
            GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken);

        public Task<AvailabilityResponse>
            GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken);

        public Task
            ReadSingleAsync(string resourcePath, int elementCount, DateTime begin, DateTime end, CancellationToken cancellationToken);
    }

    public record ApiLevelResponse(int ApiLevel);
    public record CatalogsResponse(ResourceCatalog[] Catalogs);
    public record TimeRangeResponse(DateTime Begin, DateTime End);
    public record AvailabilityResponse(double Availability);
    public record LogMessage(LogLevel LogLevel, string Message);

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
