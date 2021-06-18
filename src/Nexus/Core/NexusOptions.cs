namespace Nexus.Core
{
    // good idea: https://grafana.com/docs/grafana/latest/administration/configuration/

    public record DefaultOptions()
    {
        public static string Section { get; } = "Nexus:Default";
        public int AggregationChunkSizeMB { get; set; }
        public string DisplayName { get; set; }
    }

    public record ServerOptions()
    {
        public static string Section { get; } = "Nexus:Server";
        public string HttpScheme { get; set; }
        public string HttpAddress { get; set; }
        public int HttpPort { get; set; }
    }
}