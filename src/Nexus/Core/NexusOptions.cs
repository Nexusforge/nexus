using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Nexus.Core
{
#warning Records with IConfiguration: wait for issue https://github.com/dotnet/runtime/issues/43662 to be solved

    // template: https://grafana.com/docs/grafana/latest/administration/configuration/

    internal abstract record NexusOptionsBase()
    {
        // for testing only
        public string? BlindSample { get; set; }

        internal static IConfiguration BuildConfiguration(string[] args)
        {
            var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json");

            if (!string.IsNullOrWhiteSpace(environmentName))
            {
                builder
                    .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true);
            }

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
        public string Artifacts { get; set; } = Path.Combine(PathsOptions.PlatformSpecificRoot, "artifacts");
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

    internal record OpenIdConnectProvider
    {
#pragma warning disable CS8618 // Ein Non-Nullable-Feld muss beim Beenden des Konstruktors einen Wert ungleich NULL enthalten. Erwägen Sie die Deklaration als Nullable.
        public string Scheme { get; init; }
        public string DisplayName { get; init; }
        public string Authority { get; init; }
        public string ClientId { get; init; }
        public string ClientSecret { get; init; }
#pragma warning restore CS8618 // Ein Non-Nullable-Feld muss beim Beenden des Konstruktors einen Wert ungleich NULL enthalten. Erwägen Sie die Deklaration als Nullable.
    }

    internal partial record SecurityOptions() : NexusOptionsBase
    {
        private static string _defaultKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        public const string Section = "Security";
        public const string OicdSubSection = "Oidc";
        public string Base64JwtSigningKey { get; set; } = _defaultKey;
        public TimeSpan JwtTokenLifeTime { get; set; }
        public TimeSpan RefreshTokenLifeTime { get; set; }
        public List<OpenIdConnectProvider> OidcProviders { get; set; } = new();
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