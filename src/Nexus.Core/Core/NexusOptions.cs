using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;

namespace Nexus.Core
{
    // template: https://grafana.com/docs/grafana/latest/administration/configuration/

    public abstract record NexusOptionsBase()
    {
        // for testing only
        public string BlindSample { get; set; }

        internal static IConfiguration BuildConfiguration(string[] args)
        {
            var settingsPath = Environment.GetEnvironmentVariable("NEXUS_PATHS_SETTINGS");

            if (string.IsNullOrWhiteSpace(settingsPath))
                settingsPath = PathsOptions.DefaultSettingsPath;

            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddIniFile(settingsPath, optional: true)
                .AddImprovedEnvironmentVariables(prefix: "NEXUS_")
                .AddCommandLine(args);

            return builder.Build();
        }
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
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nexus", "nexus.conf")
            : "/etc/nexus/nexus.conf";

        public string Data { get; set; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nexus", "data")
            : "/var/lib/nexus/data";

        // GetGlobalPackagesFolder: https://github.com/NuGet/NuGet.Client/blob/0fc58e13683565e7bdf30e706d49e58fc497bbed/src/NuGet.Core/NuGet.Configuration/Utility/SettingsUtility.cs#L225-L254
        // GetFolderPath: https://github.com/NuGet/NuGet.Client/blob/1d75910076b2ecfbe5f142227cfb4fb45c093a1e/src/NuGet.Core/NuGet.Common/PathUtil/NuGetEnvironment.cs#L54-L57
        public string Packages { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexus", "packages");

        public string Attachements => Path.Combine(this.Data, "ATTACHEMENTS");
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
}