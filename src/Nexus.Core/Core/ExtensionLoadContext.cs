using NuGet.Versioning;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Environment;

namespace Nexus
{
    public class ExtensionLoadContext : AssemblyLoadContext
    {
        #region Fields

        private const int MAX_PAGES = 20;
        private const int PER_PAGE = 100;

        private static HttpClient _httpClient = new HttpClient();
        private static Regex _semVerFinder = new Regex(@"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+)?");

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

        public async Task RestoreAsync()
        {
            if (!_extensionReference.TryGetValue("Provider", out var provider))
                throw new ArgumentException("The 'Provider' parameter is missing in the extension reference.");

            switch (provider)
            {
                case "local":
                    await this.RestoreLocalAsync();
                    break;

                //case "github-releases":
                //    await this.RestoreGithubReleasesAsync();
                //    break;

                //case "gitlab-releases-v4":
                //    await this.RestoreGitLabReleasesAsync();
                //    break;

                //case "gitlab-packages-generic-v4":
                //    await this.RestoreGitLabPackagesGenericAsync();
                //    break;

                default:
                    throw new ArgumentException($"The provider '{provider}' is not supported.");
            }
        }

        public async Task<DiscoveredExtensionVersion[]> DiscoverAsync()
        {
            var result = new Dictionary<SemanticVersion, Uri>();

            if (!_extensionReference.TryGetValue("Provider", out var provider))
                throw new ArgumentException("The 'Provider' parameter is missing in the extension reference.");

            switch (provider)
            {
                case "local":
                    result = await this.DiscoverLocalAsync();
                    break;

                case "github-releases":
                    result = await this.DiscoverGithubReleasesAsync();
                    break;

                case "gitlab-releases-v4":
                    result = await this.DiscoverGitLabReleasesAsync();
                    break;

                case "gitlab-packages-generic-v4":
                    result = await this.DiscoverGitLabPackagesGenericAsync();
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

        private Task RestoreLocalAsync()
        {
            return Task.Run(() =>
            {
                var repositoryPath = ExtensionLoadContext.GetGlobalPackagesFolder();

                if (!_extensionReference.TryGetValue("Path", out var path))
                    throw new ArgumentException("The 'Path' parameter is missing in the extension reference.");

                if (!_extensionReference.TryGetValue("Version", out var version))
                    throw new ArgumentException("The 'Version' parameter is missing in the extension reference.");

                var sourcePath = Path.Combine(path, version);

                if (!Directory.Exists(sourcePath))
                    throw new DirectoryNotFoundException($"The source path '{sourcePath}' does not exist.");

                var targetPath = Path.Combine(repositoryPath, WebUtility.UrlEncode(path), version);
                Directory.CreateDirectory(targetPath);

                if (!Directory.EnumerateFileSystemEntries(targetPath).Any())
                {
                    var depsJsonFilePath = Directory
                        .EnumerateFiles(sourcePath, "*.deps.json", SearchOption.AllDirectories)
                        .SingleOrDefault();

                    if (depsJsonFilePath is null)
                    {
                        throw new Exception("The local package to restore is invalid.");
                    }
                    else
                    {
                        var dllFilePath = depsJsonFilePath
                            .Substring(0, depsJsonFilePath.Length - 10) + ".dll";

                        // we need to restore because the source folder is valid and the target folder is empty
                        if (File.Exists(dllFilePath))
                            ExtensionLoadContext.CloneFolder(sourcePath, targetPath);

                        else
                            throw new Exception("The local package to restore is invalid.");
                    }
                }
            });
        }

        private Task<Dictionary<SemanticVersion, Uri>> DiscoverLocalAsync()
        {
            var result = new Dictionary<SemanticVersion, Uri>();

            if (!_extensionReference.TryGetValue("Path", out var path))
                throw new ArgumentException("The 'Path' parameter is missing in the extension reference.");

            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"The extension path '{path}' does not exist.");

            foreach (var folderPath in Directory.EnumerateDirectories(path))
            {
                var isSemanticVersion = ExtensionLoadContext
                    .TryParseWithPrefix(Path.GetFileName(folderPath), out var version);

                if (isSemanticVersion)
                {
                    var depsJsonFilePath = Directory
                        .EnumerateFiles(folderPath, "*.deps.json", SearchOption.AllDirectories)
                        .SingleOrDefault();

                    if (depsJsonFilePath is not null)
                    {
                        var dllFilePath = depsJsonFilePath
                            .Substring(0, depsJsonFilePath.Length - 10) + ".dll";

                        if (File.Exists(dllFilePath))
                            result[version] = new Uri(folderPath);
                    }
                }
            }

            return Task.FromResult(result);
        }

        private async Task<Dictionary<SemanticVersion, Uri>> DiscoverGithubReleasesAsync()
        {
            var result = new Dictionary<SemanticVersion, Uri>();
            var ignoreCase = StringComparison.OrdinalIgnoreCase;

            if (!_extensionReference.TryGetValue("ProjectPath", out var projectPath))
                throw new ArgumentException("The 'ProjectPath' parameter is missing in the extension reference.");

            if (!_extensionReference.TryGetValue("AssetSelector", out var assetSelector))
                throw new ArgumentException("The 'AssetSelector' parameter is missing in the extension reference.");

            var server = $"https://api.github.com";
            var requestUrl = $"{server}/repos/{projectPath}/releases?per_page={PER_PAGE}&page={1}";

            for (int i = 0; i < MAX_PAGES; i++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

                if (_extensionReference.TryGetValue("Token", out var token))
                    request.Headers.Add("Authorization", $"token {token}");

                request.Headers.Add("User-Agent", "Nexus");
                request.Headers.Add("Accept", "application/vnd.github.v3+json");

                using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var jsonDocument = await JsonDocument.ParseAsync(contentStream).ConfigureAwait(false);

                foreach (var githubRelease in jsonDocument.RootElement.EnumerateArray())
                {
                    var isSemanticVersion = ExtensionLoadContext
                        .TryParseWithPrefix(githubRelease.GetProperty("name").GetString(), out var version);

                    var asset = githubRelease
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
                }

                // look for more pages
                response.Headers.TryGetValues("Link", out var links);

                if (links is null || !links.Any())
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

            return result;
        }

        private async Task<Dictionary<SemanticVersion, Uri>> DiscoverGitLabReleasesAsync()
        {
            var result = new Dictionary<SemanticVersion, Uri>();
            var ignoreCase = StringComparison.OrdinalIgnoreCase;

            if (!_extensionReference.TryGetValue("Server", out var server))
                throw new ArgumentException("The 'Server' parameter is missing in the extension reference.");

            if (!_extensionReference.TryGetValue("ProjectPath", out var projectPath))
                throw new ArgumentException("The 'ProjectPath' parameter is missing in the extension reference.");

            if (!_extensionReference.TryGetValue("AssetSelector", out var assetSelector))
                throw new ArgumentException("The 'AssetSelector' parameter is missing in the extension reference.");

            var encodedProjectPath = WebUtility.UrlEncode(projectPath);
            var requestUrl = $"{server}/api/v4/projects/{encodedProjectPath}/releases?per_page={PER_PAGE}&page={1}";

            for (int i = 0; i < MAX_PAGES; i++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

                if (_extensionReference.TryGetValue("Token", out var token))
                    request.Headers.Add("PRIVATE-TOKEN", token);

                using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var jsonDocument = await JsonDocument.ParseAsync(contentStream).ConfigureAwait(false);

                foreach (var gitlabRelease in jsonDocument.RootElement.EnumerateArray())
                {
                    var isSemanticVersion = ExtensionLoadContext
                        .TryParseWithPrefix(gitlabRelease.GetProperty("name").GetString(), out var version);

                    var asset = gitlabRelease
                        .GetProperty("assets").GetProperty("links")
                        .EnumerateArray()
                        .FirstOrDefault(current => Regex.IsMatch(current.GetProperty("name").GetString(), assetSelector));

                    if (isSemanticVersion && asset.ValueKind != JsonValueKind.Undefined)
                    {
                        var assetUri = new Uri(asset.GetProperty("direct_asset_url").GetString());

                        var isValidAssetType =
                            assetUri.ToString().EndsWith("zip", ignoreCase) ||
                            assetUri.ToString().EndsWith("tar.gz", ignoreCase);

                        result[version] = assetUri;
                    }
                }

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

            return result;
        }

        private async Task<Dictionary<SemanticVersion, Uri>> DiscoverGitLabPackagesGenericAsync()
        {
            var result = new ConcurrentDictionary<SemanticVersion, Uri>();
            var ignoreCase = StringComparison.OrdinalIgnoreCase;

            if (!_extensionReference.TryGetValue("Server", out var server))
                throw new ArgumentException("The 'Server' parameter is missing in the extension reference.");

            if (!_extensionReference.TryGetValue("ProjectPath", out var projectPath))
                throw new ArgumentException("The 'ProjectPath' parameter is missing in the extension reference.");

            if (!_extensionReference.TryGetValue("Package", out var package))
                throw new ArgumentException("The 'Package' parameter is missing in the extension reference.");

            if (!_extensionReference.TryGetValue("AssetSelector", out var assetSelector))
                throw new ArgumentException("The 'AssetSelector' parameter is missing in the extension reference.");

            var encodedProjectPath = WebUtility.UrlEncode(projectPath);
            var requestUrl = $"{server}/api/v4/projects/{encodedProjectPath}/packages?per_page={PER_PAGE}&page={1}";

            for (int i = 0; i < MAX_PAGES; i++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

                if (_extensionReference.TryGetValue("Token", out var token))
                    request.Headers.Add("PRIVATE-TOKEN", token);

                using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                var message =await response.Content.ReadAsStringAsync();

                var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var jsonDocument = await JsonDocument.ParseAsync(contentStream).ConfigureAwait(false);

                var selectedGitlabPackages = jsonDocument
                    .RootElement
                    .EnumerateArray()
                    .Where(gitlabPackage =>
                    {
                        var isCorrectPackage = gitlabPackage.GetProperty("name").GetString() == package;

                        var isSemanticVersion = ExtensionLoadContext
                            .TryParseWithPrefix(gitlabPackage.GetProperty("version").GetString(), out var version);

                        return isCorrectPackage & isSemanticVersion;
                    })
                    .ToList();

                var tasks = selectedGitlabPackages.Select(async gitlabPackage =>
                {
                    var isSemanticVersion = ExtensionLoadContext
                        .TryParseWithPrefix(gitlabPackage.GetProperty("version").GetString(), out var version);

                    var packageId = gitlabPackage.GetProperty("id").GetInt32();
                    var packageVersion = gitlabPackage.GetProperty("version").GetString();
                    var assetRequestUrl = $"{server}/api/v4/projects/{encodedProjectPath}/packages/{packageId}/package_files";

                    using var assetRequest = new HttpRequestMessage(HttpMethod.Get, assetRequestUrl);

                    if (_extensionReference.TryGetValue("Token", out var token))
                        assetRequest.Headers.Add("PRIVATE-TOKEN", token);

                    using var assetResponse = await _httpClient.SendAsync(assetRequest).ConfigureAwait(false);

                    assetResponse.EnsureSuccessStatusCode();

                    var assetContentStream = await assetResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    var assetJsonDocument = await JsonDocument.ParseAsync(assetContentStream).ConfigureAwait(false);

                    var asset = assetJsonDocument
                        .RootElement
                        .EnumerateArray()
                        .FirstOrDefault(current => Regex.IsMatch(current.GetProperty("file_name").GetString(), assetSelector));

                    if (asset.ValueKind != JsonValueKind.Undefined)
                    {
                        // Generic Package Registry - Download package file
                        // https://docs.gitlab.com/ee/user/packages/generic_packages/#download-package-file
                        var fileName = asset.GetProperty("file_name").GetString();
                        var assetUri = new Uri($"{server}/api/v4/projects/{encodedProjectPath}/packages/generic/{package}/{packageVersion}/{fileName}");

                        var isValidAssetType =
                            assetUri.ToString().EndsWith("zip", ignoreCase) ||
                            assetUri.ToString().EndsWith("tar.gz", ignoreCase);

                        result.TryAdd(version, assetUri);
                    }
                });

                await Task.WhenAll(tasks);

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

            return result
                .ToDictionary(entry => entry.Key, entry => entry.Value);
        }

        // GetGlobalPackagesFolder: https://github.com/NuGet/NuGet.Client/blob/0fc58e13683565e7bdf30e706d49e58fc497bbed/src/NuGet.Core/NuGet.Configuration/Utility/SettingsUtility.cs#L225-L254
        // GetFolderPath: https://github.com/NuGet/NuGet.Client/blob/1d75910076b2ecfbe5f142227cfb4fb45c093a1e/src/NuGet.Core/NuGet.Common/PathUtil/NuGetEnvironment.cs#L54-L57
        private static string GetGlobalPackagesFolder()
        {
            var home = Path.Combine(Environment.GetFolderPath(SpecialFolder.UserProfile), ".nuget");
            var path = Path.Combine(home, "packages");

            return path;
        }

        private static bool TryParseWithPrefix(string value, out SemanticVersion version)
        {
            version = default;
            var match = _semVerFinder.Match(value);

            if (match.Success)
            {
                version = SemanticVersion.Parse(match.Value);
                return true;
            }

            return false;
        }

        private static void CloneFolder(string source, string target)
        {
            if (!Directory.Exists(source))
                throw new Exception("The source directory does not exist.");

            if (!Directory.Exists(target))
                throw new Exception("The target directory does not exist.");

            var sourceInfo = new DirectoryInfo(source);
            var targetInfo = new DirectoryInfo(target);

            if (sourceInfo.FullName == targetInfo.FullName)
                throw new Exception("Source and destination are the same.");

            foreach (var folderPath in Directory.GetDirectories(source))
            {
                var folderName = Path.GetFileName(folderPath);

                Directory.CreateDirectory(Path.Combine(target, folderName));
                ExtensionLoadContext.CloneFolder(folderPath, Path.Combine(target, folderName));
            }

            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            }
        }

        #endregion
    }
}
