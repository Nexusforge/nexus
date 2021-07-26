using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
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

        private const int MAX_PAGES = 20;

        private static HttpClient _httpClient = new HttpClient();

        private bool _isInitialized;
        private AssemblyDependencyResolver _resolver;

        private Dictionary<string, string> _extensionReference;

        #endregion

        #region Constructors

        public ExtensionLoadContext(Dictionary<string, string> extensionReference)
        {
            _extensionReference = extensionReference;
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

            if (!_extensionReference.TryGetValue("Provider", out var provider))
                throw new ArgumentException("The 'Provider' parameter is missing in the extension reference.");

            switch (provider)
            {
                case "GitHub":
                    result = await this.DiscoverGithubVersionsAsync();
                    break;

                case "GitLab":
                    result = await this.DiscoverGitLabVersionsAsync();
                    break;

                case "Local":
                    break;

                default:
                    throw new ArgumentException($"The provider '{provider}' is not supported.");
            }

            var comparer = new VersionComparer();

            return result
                .OrderBy(entry => entry.Key, comparer)
                .Select(entry => new DiscoveredExtensionVersion(entry.Key.ToNormalizedString(), entry.Value.ToString()))
                .ToArray();
        }

        private async Task<Dictionary<SemanticVersion, Uri>> DiscoverGithubVersionsAsync()
        {
            var result = new Dictionary<SemanticVersion, Uri>();
            var ignoreCase = StringComparison.OrdinalIgnoreCase;

            if (!_extensionReference.TryGetValue("User", out var user))
                throw new ArgumentException("The 'User' parameter is missing in the extension reference.");

            if (!_extensionReference.TryGetValue("Project", out var project))
                throw new ArgumentException("The 'Project' parameter is missing in the extension reference.");

            if (!_extensionReference.TryGetValue("Release", out var release))
                throw new ArgumentException("The 'Release' parameter is missing in the extension reference.");

            if (!_extensionReference.TryGetValue("AssetSelector", out var assetSelector))
                throw new ArgumentException("The 'AssetSelector' parameter is missing in the extension reference.");

            var host = $"https://api.github.com";
            var requestUrl = $"{host}/repos/{user}/{project}/releases?per_page={100}&page={1}";

            for (int i = 0; i < MAX_PAGES; i++)
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, requestUrl))
                {
                    request.Headers.Add("User-Agent", "Nexus");
                    request.Headers.Add("Accept", "application/vnd.github.v3+json");

                    using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();

                        var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        var jsonDocument = await JsonDocument.ParseAsync(contentStream).ConfigureAwait(false);

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
                                    .FirstOrDefault(current => Regex.IsMatch(current.GetProperty("name").GetString(), assetSelector));

                                if (isSemanticVersion && asset.ValueKind != JsonValueKind.Undefined)
                                {
                                    var assetUri = new Uri(asset.GetProperty("browser_download_url").GetString());

                                    var isValidAssetType =
                                        assetUri.ToString().EndsWith("zip", ignoreCase) ||
                                        assetUri.ToString().EndsWith("tar.gz", ignoreCase);

                                    result[version] = assetUri;
                                }
                            });

                        // look for more pages
                        response.Headers.TryGetValues("Link", out var links);

                        if (!links.Any())
                            break;

                        requestUrl = links
                            .First()
                            .Split(",")
                            .Where(current => current.Contains("rel=\"next\""))
                            .Select(current => Regex.Match(current, @"\<(https:.*)\>; rel=""next""").Groups[1].Value)
                            .FirstOrDefault();

                        if (requestUrl == default)
                            break;

                        continue;
                    }
                }
            }

            return result;
        }

        private async Task<Dictionary<SemanticVersion, Uri>> DiscoverGitLabVersionsAsync()
        {
            var result = new Dictionary<SemanticVersion, Uri>();
            var ignoreCase = StringComparison.OrdinalIgnoreCase;

            if (!_extensionReference.TryGetValue("Server", out var server))
                throw new ArgumentException("The 'Server' parameter is missing in the extension reference.");

            if (!_extensionReference.TryGetValue("User", out var user))
                throw new ArgumentException("The 'User' parameter is missing in the extension reference.");

            if (!_extensionReference.TryGetValue("Project", out var project))
                throw new ArgumentException("The 'Project' parameter is missing in the extension reference.");

            if (!_extensionReference.TryGetValue("Release", out var release))
                throw new ArgumentException("The 'Release' parameter is missing in the extension reference.");

            if (!_extensionReference.TryGetValue("AssetSelector", out var assetSelector))
                throw new ArgumentException("The 'AssetSelector' parameter is missing in the extension reference.");

            var host = $"https://api.github.com";
            var requestUrl = $"{host}/repos/{user}/{project}/releases?per_page={100}&page={1}";

            for (int i = 0; i < MAX_PAGES; i++)
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, requestUrl))
                {
                    request.Headers.Add("User-Agent", "Nexus");
                    request.Headers.Add("Accept", "application/vnd.github.v3+json");

                    using (var response = await _httpClient.SendAsync(request).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();

                        var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        var jsonDocument = await JsonDocument.ParseAsync(contentStream).ConfigureAwait(false);

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
                                    .FirstOrDefault(current => Regex.IsMatch(current.GetProperty("name").GetString(), assetSelector));

                                if (isSemanticVersion && asset.ValueKind != JsonValueKind.Undefined)
                                {
                                    var assetUri = new Uri(asset.GetProperty("browser_download_url").GetString());

                                    var isValidAssetType =
                                        assetUri.ToString().EndsWith("zip", ignoreCase) ||
                                        assetUri.ToString().EndsWith("tar.gz", ignoreCase);

                                    result[version] = assetUri;
                                }
                            });

                        // look for more pages
                        response.Headers.TryGetValues("Link", out var links);

                        if (!links.Any())
                            break;

                        requestUrl = links
                            .First()
                            .Split(",")
                            .Where(current => current.Contains("rel=\"next\""))
                            .Select(current => Regex.Match(current, @"\<(https:.*)\>; rel=""next""").Groups[1].Value)
                            .FirstOrDefault();

                        if (requestUrl == default)
                            break;

                        continue;
                    }
                }
            }

            return result;
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

        #endregion
    }
}
