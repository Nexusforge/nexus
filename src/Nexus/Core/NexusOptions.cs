using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using static System.Environment;

namespace Nexus.Core
{
    // template: https://grafana.com/docs/grafana/latest/administration/configuration/

    public abstract record NexusOptionsBase()
    {
        // for testing only
        public string BlindSample { get; set; }
    }

    public record GeneralOptions() : NexusOptionsBase
    {
        public const string Section = "General";
        public string InstanceName { get; } = Dns.GetHostName();
        public string Language { get; }
    }

    public record ServerOptions() : NexusOptionsBase
    {
        public const string Section = "Server";
        public string HttpScheme { get; set; }
        public string HttpAddress { get; set; }
        public int HttpPort { get; set; }
    }

    public record PathsOptions() : NexusOptionsBase
    {
        public const string Section = "Paths";

        public static string DefaultSettingsPath { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), "Nexus", "nexus.conf")
            : "/etc/nexus/nexus.conf";

        public string Data { get; set; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), "Nexus", "data")
            : "/var/lib/nexus/data";

        public string Export => Path.Combine(this.Data, "EXPORT");
    }

    public record SecurityOptions() : NexusOptionsBase
    {
        public const string Section = "Security";
        public static string DefaultRootUser { get; } = "root@nexus.localhost";
        public static string DefaultRootPassword { get; } = "#root0/User1";
        public string RootUser { get; set; } = SecurityOptions.DefaultRootPassword;
        public string RootPassword { get; set; } = SecurityOptions.DefaultRootPassword;
    }

    public record UsersOptions() : NexusOptionsBase
    {
        public const string Section = "Users";
        public bool VerifyEmail { get; set; }
    }

    public record SmtpOptions : NexusOptionsBase
    {
        public const string Section = "Smtp";
        public string Host { get; set; }
        public ushort Port { get; set; }
        public string FromAddress { get; set; }
        public string FromName { get; set; }
    }

    public record AggregationOptions() : NexusOptionsBase
    {
        public const string Section = "Aggregation";
        public uint ChunkSizeMB { get; set; }
    }
}