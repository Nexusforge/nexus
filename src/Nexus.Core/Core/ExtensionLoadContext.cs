using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Nexus
{
    public class ExtensionLoadContext : AssemblyLoadContext
    {
        #region Fields

        private static HttpClient _httpClient = new HttpClient();
        private static Regex _regex = new Regex(@"^\/(.*)\/(.*)\/releases\/download\/(.*)\/(.*\.(zip|tar\.gz))$");

        private bool _isInitialized;
        private Uri _resourceLocator;
        private AssemblyDependencyResolver _resolver;

        #endregion

        #region Constructors

        public ExtensionLoadContext(Uri resourceLocator)
        {
            _resourceLocator = resourceLocator;
        }

        #endregion

        #region Methods

        protected override Assembly Load(AssemblyName assemblyName)
        {
            if (!_isInitialized)
                this.Initialize();

            string assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);

            if (assemblyPath != null)
                return LoadFromAssemblyPath(assemblyPath);

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            if (!_isInitialized)
                this.Initialize();

            string libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

            if (libraryPath != null)
                return LoadUnmanagedDllFromPath(libraryPath);

            return IntPtr.Zero;
        }

        private void Initialize()
        {


            //_resolver = new AssemblyDependencyResolver(pluginPath);
            _isInitialized = true;
        }

        public async Task<DiscoveredExtensionVersion[]> DiscoverVersionsAsync()
        {
            var result = new Dictionary<SemanticVersion, Uri>();
            var ignoreCase = StringComparison.OrdinalIgnoreCase;

            if (_resourceLocator.Scheme.Equals("http", ignoreCase) ||
                _resourceLocator.Scheme.Equals("https", ignoreCase))
            {
                if (_resourceLocator.Host.Equals("github.com", ignoreCase))
                {
                    try
                    {
                        await ExtensionLoadContext.DiscoverGithubVersionsAsync(_resourceLocator, result);
                    }
                    catch
                    {
                        //
                    }
                }
            }

            var comparer = new VersionComparer();

            return result
                .OrderBy(entry => entry.Key, comparer)
                .Select(entry => new DiscoveredExtensionVersion(entry.Key.ToNormalizedString(), entry.Value.ToString()))
                .ToArray();
        }

        private static bool TryParseWithPrefix(string value, out SemanticVersion version)
        {
            version = default;

            while (value.Length > 0)
            {
                if (SemanticVersion.TryParse(value, out version))
                    return true;

                value = value.Substring(1);
            }

            return false;
        }

        private static async Task DiscoverGithubVersionsAsync(Uri resourceLocator, Dictionary<SemanticVersion, Uri> result)
        {
            var ignoreCase = StringComparison.OrdinalIgnoreCase;
            var match = _regex.Match(resourceLocator.PathAndQuery);

            if (match.Success)
            {
                var user = match.Groups[1].Value;
                var project = match.Groups[2].Value;
                var versionString = match.Groups[3].Value;
                var assetName = match.Groups[4].Value;
                var assetType = match.Groups[5].Value;

                var isValidAssetType =
                    assetType.Equals("zip", ignoreCase) ||
                    assetType.Equals("tar.gz", ignoreCase);

                if (isValidAssetType && ExtensionLoadContext.TryParseWithPrefix(versionString, out var version))
                {
                    var host = $"{resourceLocator.Scheme}://api.github.com";
                    var requestUrl = $"{host}/repos/{user}/{project}/releases";

                    using (var request = new HttpRequestMessage(HttpMethod.Get, requestUrl))
                    {
                        request.Headers.Add("User-Agent", "Nexus");
                        request.Headers.Add("Accept", "application/vnd.github.v3+json");

                        using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                        {
                            response.EnsureSuccessStatusCode();

                            var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

                            try
                            {
                                var jsonDocument = await JsonDocument.ParseAsync(contentStream);

                                jsonDocument.RootElement
                                    .EnumerateArray()
                                    .ToList()
                                    .ForEach(release =>
                                    {
                                        var isSemanticVersion = ExtensionLoadContext
                                            .TryParseWithPrefix(release.GetProperty("name").GetString(), out var version);

                                        var asset = release
                                            .GetProperty("assets")
                                            .EnumerateArray()
                                            .FirstOrDefault(current => current
                                                .GetProperty("name")
                                                .GetString()
                                                .EndsWith($".{assetType}"));

                                        if (isSemanticVersion && asset.ValueKind != JsonValueKind.Undefined)
                                        {
                                            var assetUri = new Uri(asset.GetProperty("browser_download_url").GetString());
                                            result[version] = assetUri;
                                        }
                                    });
                            }
                            catch (JsonException)
                            {
                                Console.WriteLine("Invalid JSON response.");
                            }
                        }
                    }
                }
            }
        }

        #endregion
    }
}
