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
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json");

            var settingsPath = Environment.GetEnvironmentVariable("NEXUS_PATHS__SETTINGS");

            if (settingsPath is null)
                settingsPath = PathsOptions.DefaultSettingsPath;

            if (settingsPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                builder.AddJsonFile(settingsPath, optional: true);

            builder
                .AddEnvironmentVariables(prefix: "NEXUS_")
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

        public string DbConfig { get; set; } = Path.Combine(PathsOptions.GetDataRoot(), "dbconfig.json");
        public string Cache { get; set; } = Path.Combine(PathsOptions.GetDataRoot(), "cache");
        public string Catalogs { get; set; } = Path.Combine(PathsOptions.GetDataRoot(), "catalogs");
        public string Export { get; set; } = Path.Combine(PathsOptions.GetDataRoot(), "export");
        public string Users { get; set; } = Path.Combine(PathsOptions.GetDataRoot(), "users");
        public string Packages { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexus", "packages");
        // GetGlobalPackagesFolder: https://github.com/NuGet/NuGet.Client/blob/0fc58e13683565e7bdf30e706d49e58fc497bbed/src/NuGet.Core/NuGet.Configuration/Utility/SettingsUtility.cs#L225-L254
        // GetFolderPath: https://github.com/NuGet/NuGet.Client/blob/1d75910076b2ecfbe5f142227cfb4fb45c093a1e/src/NuGet.Core/NuGet.Common/PathUtil/NuGetEnvironment.cs#L54-L57

        #region Support

        public static string DefaultSettingsPath { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nexus", "settings.json")
            : "/etc/nexus/settings.json";

        private static string GetDataRoot() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nexus", "data")
            : "/var/lib/nexus/data";

        #endregion
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