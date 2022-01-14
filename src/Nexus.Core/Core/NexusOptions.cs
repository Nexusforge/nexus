using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Nexus.Core
{
    // template: https://grafana.com/docs/grafana/latest/administration/configuration/

    internal abstract record NexusOptionsBase()
    {
        // for testing only
        public string? BlindSample { get; set; }

        internal static IConfiguration BuildConfiguration(string[] args)
        {
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json");

            var settingsPath = Environment.GetEnvironmentVariable("NEXUS_PATHS__SETTINGS");

            if (settingsPath is null)
                settingsPath = PathsOptions.DefaultSettingsPath;

            if (settingsPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                builder.AddJsonFile(settingsPath, optional: true, /* for serilog */ reloadOnChange: true);

            builder
                .AddEnvironmentVariables(prefix: "NEXUS_")
                .AddCommandLine(args);

            return builder.Build();
        }
    }

    internal record GeneralOptions() : NexusOptionsBase
    {
        public const string Section = "General";
        public string? ApplicationName { get; set; }
    }

    internal record ServerOptions() : NexusOptionsBase
    {
        public const string Section = "Server";
        public string? HttpScheme { get; set; }
        public string? HttpAddress { get; set; }
        public int HttpPort { get; set; }
    }

    internal record PathsOptions() : NexusOptionsBase
    {
        public const string Section = "Paths";

        public string Config { get; set; } = Path.Combine(PathsOptions.PlatformSpecificRoot, "config");
        public string Cache { get; set; } = Path.Combine(PathsOptions.PlatformSpecificRoot, "cache");
        public string Catalogs { get; set; } = Path.Combine(PathsOptions.PlatformSpecificRoot, "catalogs");
        public string Export { get; set; } = Path.Combine(PathsOptions.PlatformSpecificRoot, "export");
        public string Users { get; set; } = Path.Combine(PathsOptions.PlatformSpecificRoot, "users");
        public string Packages { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexus", "packages");
        // GetGlobalPackagesFolder: https://github.com/NuGet/NuGet.Client/blob/0fc58e13683565e7bdf30e706d49e58fc497bbed/src/NuGet.Core/NuGet.Configuration/Utility/SettingsUtility.cs#L225-L254
        // GetFolderPath: https://github.com/NuGet/NuGet.Client/blob/1d75910076b2ecfbe5f142227cfb4fb45c093a1e/src/NuGet.Core/NuGet.Common/PathUtil/NuGetEnvironment.cs#L54-L57

        #region Support

        public static string DefaultSettingsPath { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nexus", "settings.json")
            : "/etc/nexus/settings.json";

        private static string PlatformSpecificRoot { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nexus")
            : "/var/lib/nexus";

        #endregion
    }

    internal record SecurityOptions() : NexusOptionsBase
    {
        private static string _defaultKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        public const string Section = "Security";
        public static string DefaultRootUser { get; } = "root@nexus.localhost";
        public static string DefaultRootPassword { get; } = "#root0/User1";
        public string RootUser { get; set; } = SecurityOptions.DefaultRootUser;
        public string RootPassword { get; set; } = SecurityOptions.DefaultRootPassword;
        public string Base64JwtSigningKey { get; set; } = _defaultKey;
        public TimeSpan JwtTokenLifeTime { get; set; }
        public TimeSpan RefreshTokenLifeTime { get; set; }
    }

    internal record UsersOptions() : NexusOptionsBase
    {
        public const string Section = "Users";
        public bool VerifyEmail { get; set; }
    }

    internal record SmtpOptions : NexusOptionsBase
    {
        public const string Section = "Smtp";
        public string? Host { get; set; }
        public ushort Port { get; set; }
        public string? FromAddress { get; set; }
        public string? FromName { get; set; }
    }
}