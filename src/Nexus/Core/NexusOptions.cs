using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using static System.Environment;

namespace Nexus.Core
{
    // template: https://grafana.com/docs/grafana/latest/administration/configuration/

    public record GeneralOptions()
    {
        public static string Section { get; } = "Nexus.General";
        public string InstanceName { get; } = Dns.GetHostName();
        public string Language { get; }
    }

    public record ServerOptions()
    {
        public static string Section { get; } = "Nexus:Server";
        public string HttpScheme { get; set; }
        public string HttpAddress { get; set; }
        public int HttpPort { get; set; }
    }

    public record PathsOptions()
    {
        public static string Section { get; } = "Nexus:Paths";

        public static string DefaultSettingsPath { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), "Nexus", "nexus.conf")
            : "/etc/nexus/nexus.conf";

        public string Data { get; set; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), "Nexus", "data")
            : "/var/lib/nexus/data";

        public string Extensions { get; set; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), "Nexus", "extensions")
            : "/var/lib/nexus/extensions";

        public string Export => Path.Combine(this.Data, "EXPORT");
    }

    public record SecurityOptions()
    {
        public static string Section { get; } = "Nexus:Security";
        public static string DefaultRootUser { get; } = "root@nexus.localhost";
        public static string DefaultRootPassword { get; } = "#root0/User1";
        public string RootUser { get; set; } = SecurityOptions.DefaultRootPassword;
        public string RootPassword { get; set; } = SecurityOptions.DefaultRootPassword;
    }

    public record UsersOptions()
    {
        public static string Section { get; } = "Nexus:Users";
        public bool VerifyEmail { get; set; }
    }

    public record SmtpOptions
    {
        public static string Section { get; } = "Nexus:Smtp";
        public string Host { get; set; }
        public ushort Port { get; set; }
        public string FromAddress { get; set; }
        public string FromName { get; set; }
    }

    public record AggregationOptions()
    {
        public static string Section { get; } = "Nexus:Aggregation";
        public uint ChunkSizeMB { get; set; }
    }
}