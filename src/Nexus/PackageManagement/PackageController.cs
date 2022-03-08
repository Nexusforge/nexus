﻿using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using Nexus.Core;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nexus.PackageManagement
{
    internal class PackageController
    {
        #region Fields

        private const int MAX_PAGES = 20;
        private const int PER_PAGE = 100;

        private static HttpClient _httpClient = new HttpClient();

        private ILogger _logger;
        private PackageLoadContext? _loadContext;

        #endregion

        #region Constructors

        public PackageController(PackageReference packageReference, ILogger<PackageController> logger)
        {
            PackageReference = packageReference;
            _logger = logger;
        }

        #endregion

        #region Properties

        public PackageReference PackageReference { get; }

        #endregion

        #region Methods

        public async Task<string[]> DiscoverAsync(CancellationToken cancellationToken)
        {
            string[] result;

            _logger.LogDebug("Discover package versions using provider {Provider}", PackageReference.Provider);

            switch (PackageReference.Provider)
            {
                case "local":
                    result = await DiscoverLocalAsync(cancellationToken);
                    break;

                case "github-releases":
                    result = await DiscoverGithubReleasesAsync(cancellationToken);
                    break;

                case "gitlab-packages-generic-v4":
                    result = await DiscoverGitLabPackagesGenericAsync(cancellationToken);
                    break;

                /* this approach does not work, see rationale below (#region gitlab-releases-v4) */

                //case "gitlab-releases-v4":
                //    result = await DiscoverGitLabReleasesAsync(cancellationToken);
                //    break;

                default:
                    throw new ArgumentException($"The provider {PackageReference.Provider} is not supported.");
            }

            return result;
        }

        public async Task<Assembly> LoadAsync(string restoreRoot, CancellationToken cancellationToken)
        {
            if (_loadContext is not null)
                throw new Exception("The extension is already loaded.");

            var restoreFolderPath = await RestoreAsync(restoreRoot, cancellationToken);
            var depsJsonExtension = ".deps.json";

            var depsJsonFilePath = Directory
                .EnumerateFiles(restoreFolderPath, $"*{depsJsonExtension}", SearchOption.AllDirectories)
                .SingleOrDefault();

            if (depsJsonFilePath is null)
                throw new Exception($"Could not determine the location of the .deps.json file in folder {restoreFolderPath}.");

            var entryDllPath = depsJsonFilePath.Substring(0, depsJsonFilePath.Length - depsJsonExtension.Length) + ".dll";

            if (entryDllPath is null)
                throw new Exception($"Could not determine the location of the entry DLL file in folder {restoreFolderPath}.");

            _loadContext = new PackageLoadContext(entryDllPath);

            var assemblyName = new AssemblyName(Path.GetFileNameWithoutExtension(entryDllPath));
            var assembly = _loadContext.LoadFromAssemblyName(assemblyName);

            return assembly;
        }

        public WeakReference Unload()
        {
            if (_loadContext is null)
                throw new Exception("The extension is not yet loaded.");

            _loadContext.Unload();
            var weakReference = new WeakReference(_loadContext, trackResurrection: true);
            _loadContext = null;

            return weakReference;
        }

        internal async Task<string> RestoreAsync(string restoreRoot, CancellationToken cancellationToken)
        {
            string restoreFolderPath;
            var actualRestoreRoot = Path.Combine(restoreRoot, PackageReference.Provider);

            _logger.LogDebug("Restore package to {RestoreRoot} using provider {Provider}", actualRestoreRoot, PackageReference.Provider);

            switch (PackageReference.Provider)
            {
                case "local":
                    restoreFolderPath = await RestoreLocalAsync(actualRestoreRoot, cancellationToken);
                    break;

                case "github-releases":
                    restoreFolderPath = await RestoreGitHubReleasesAsync(actualRestoreRoot, cancellationToken);
                    break;

                case "gitlab-packages-generic-v4":
                    restoreFolderPath = await RestoreGitLabPackagesGenericAsync(actualRestoreRoot, cancellationToken);
                    break;

                /* this approach does not work, see rationale below (#region gitlab-releases-v4) */

                //case "gitlab-releases-v4":
                //    restoreFolderPath = await RestoreGitLabReleasesAsync(actualRestoreRoot, cancellationToken);
                //    break;

                default:
                    throw new ArgumentException($"The provider {PackageReference.Provider} is not supported.");
            }

            return restoreFolderPath;
        }

        private static void CloneFolder(string source, string target)
        {
            if (!Directory.Exists(source))
                throw new Exception("The source directory does not exist.");

            Directory.CreateDirectory(target);

            var sourceInfo = new DirectoryInfo(source);
            var targetInfo = new DirectoryInfo(target);

            if (sourceInfo.FullName == targetInfo.FullName)
                throw new Exception("Source and destination are the same.");

            foreach (var folderPath in Directory.GetDirectories(source))
            {
                var folderName = Path.GetFileName(folderPath);

                Directory.CreateDirectory(Path.Combine(target, folderName));
                CloneFolder(folderPath, Path.Combine(target, folderName));
            }

            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            }
        }

        private static async Task DownloadAndExtractAsync(
            string assetName,
            string assetUrl,
            string targetPath,
            Dictionary<string, string> headers)
        {
            // get download stream
            async Task<HttpResponseMessage> GetAssetResponseAsync()
            {
                using var assetRequest = new HttpRequestMessage(HttpMethod.Get, assetUrl);

                foreach (var entry in headers)
                {
                    assetRequest.Headers.Add(entry.Key, entry.Value);
                }

                var assetResponse = await _httpClient
                    .SendAsync(assetRequest, HttpCompletionOption.ResponseHeadersRead);

                assetResponse.EnsureSuccessStatusCode();

                return assetResponse;
            }

            // download and extract
            if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var assetResponse = await GetAssetResponseAsync();
                using var stream = await assetResponse.Content.ReadAsStreamAsync();
                using var zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
                zipArchive.ExtractToDirectory(targetPath);
            }
            else if (assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                using var assetResponse = await GetAssetResponseAsync();
                using var stream = await assetResponse.Content.ReadAsStreamAsync();
                using var gzipStream = new GZipInputStream(stream);
                using var tarArchive = TarArchive.CreateInputTarArchive(gzipStream, Encoding.UTF8);
                tarArchive.ExtractContents(targetPath);
            }
            else
            {
                throw new Exception("Only assets of type .zip or .tar.gz are supported.");
            }
        }

        #endregion

        #region local

        private Task<string[]> DiscoverLocalAsync(CancellationToken cancellationToken)
        {
            var rawResult = new List<string>();
            var configuration = PackageReference.Configuration;

            if (!configuration.TryGetValue("Path", out var path))
                throw new ArgumentException("The 'Path' parameter is missing in the extension reference.");

            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"The extension path {path} does not exist.");

            foreach (var folderPath in Directory.EnumerateDirectories(path))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var folderName = Path.GetFileName(folderPath);
                rawResult.Add(folderName);
                _logger.LogDebug("Discovered package version {PackageVersion}", folderName);
            }

            var result = rawResult.OrderBy(value => value).Reverse();

            return Task.FromResult(result.ToArray());
        }

        private Task<string> RestoreLocalAsync(string restoreRoot, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var configuration = PackageReference.Configuration;

                if (!configuration.TryGetValue("Path", out var path))
                    throw new ArgumentException("The 'Path' parameter is missing in the extension reference.");

                if (!configuration.TryGetValue("Version", out var version))
                    throw new ArgumentException("The 'Version' parameter is missing in the extension reference.");

                var sourcePath = Path.Combine(path, version);

                if (!Directory.Exists(sourcePath))
                    throw new DirectoryNotFoundException($"The source path {sourcePath} does not exist.");

                var pathHash = new Guid(path.Hash()).ToString();
                var targetPath = Path.Combine(restoreRoot, pathHash, version);

                if (!Directory.Exists(targetPath) || !Directory.EnumerateFileSystemEntries(targetPath).Any())
                {
                    _logger.LogDebug("Restore package from source {Source} to {Target}", sourcePath, targetPath);
                    CloneFolder(sourcePath, targetPath);
                }
                else
                {
                    _logger.LogDebug("Package is already restored");
                }

                return targetPath;
            }, cancellationToken);
        }

        #endregion

        #region github-releases

        private async Task<string[]> DiscoverGithubReleasesAsync(CancellationToken cancellationToken)
        {
            var result = new List<string>();
            var configuration = PackageReference.Configuration;

            if (!configuration.TryGetValue("ProjectPath", out var projectPath))
                throw new ArgumentException("The 'ProjectPath' parameter is missing in the extension reference.");

            var server = $"https://api.github.com";
            var requestUrl = $"{server}/repos/{projectPath}/releases?per_page={PER_PAGE}&page={1}";

            for (int i = 0; i < MAX_PAGES; i++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

                if (configuration.TryGetValue("Token", out var token))
                    request.Headers.Add("Authorization", $"token {token}");

                request.Headers.Add("User-Agent", "Nexus");
                request.Headers.Add("Accept", "application/vnd.github.v3+json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var jsonDocument = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);

                foreach (var githubRelease in jsonDocument.RootElement.EnumerateArray())
                {
                    var releaseTagName = githubRelease.GetProperty("tag_name").GetString() ?? throw new Exception("tag_name is null");
                    result.Add(releaseTagName);
                    _logger.LogDebug("Discovered package version {PackageVersion}", releaseTagName);
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

            return result.ToArray();
        }

        private async Task<string> RestoreGitHubReleasesAsync(string restoreRoot, CancellationToken cancellationToken)
        {
            var configuration = PackageReference.Configuration;

            if (!configuration.TryGetValue("ProjectPath", out var projectPath))
                throw new ArgumentException("The 'ProjectPath' parameter is missing in the extension reference.");

            if (!configuration.TryGetValue("Tag", out var tag))
                throw new ArgumentException("The 'Tag' parameter is missing in the extension reference.");

            if (!configuration.TryGetValue("AssetSelector", out var assetSelector))
                throw new ArgumentException("The 'AssetSelector' parameter is missing in the extension reference.");

            var targetPath = Path.Combine(restoreRoot, projectPath.Replace('/', '_').ToLower(), tag);

            if (!Directory.Exists(targetPath) || !Directory.EnumerateFileSystemEntries(targetPath).Any())
            {
                var server = $"https://api.github.com";
                var requestUrl = $"{server}/repos/{projectPath}/releases/tags/{tag}";

                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

                if (configuration.TryGetValue("Token", out var token))
                    request.Headers.Add("Authorization", $"token {token}");

                request.Headers.Add("User-Agent", "Nexus");
                request.Headers.Add("Accept", "application/vnd.github.v3+json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var jsonDocument = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);

                // find asset
                var gitHubRelease = jsonDocument.RootElement;
                var releaseTagName = gitHubRelease.GetProperty("tag_name").GetString();

                var asset = gitHubRelease
                    .GetProperty("assets")
                    .EnumerateArray()
                    .FirstOrDefault(current => Regex.IsMatch(current.GetProperty("name").GetString() ?? throw new Exception("assets is null"), assetSelector));

                if (asset.ValueKind != JsonValueKind.Undefined)
                {
                    // get asset download URL
                    var assetUrl = asset.GetProperty("url").GetString() ?? throw new Exception("url is null");
                    var assetBrowserUrl = asset.GetProperty("browser_download_url").GetString() ?? throw new Exception("browser_download_url is null");

                    // get download stream
                    var headers = new Dictionary<string, string>();

                    if (configuration.TryGetValue("Token", out var assetToken))
                        headers["Authorization"] = $"token {assetToken}";

                    headers["User-Agent"] = "Nexus";
                    headers["Accept"] = "application/octet-stream";

                    _logger.LogDebug("Restore package from source {Source} to {Target}", assetBrowserUrl, targetPath);
                    await DownloadAndExtractAsync(assetBrowserUrl, assetUrl, targetPath, headers);
                }
                else
                {
                    throw new Exception("No matching assets found.");
                }
            }
            else
            {
                _logger.LogDebug("Package is already restored");
            }

            return targetPath;
        }

        #endregion

        #region gitlab-packages-generic-v4

        private async Task<string[]> DiscoverGitLabPackagesGenericAsync(CancellationToken cancellationToken)
        {
            var result = new List<string>();
            var configuration = PackageReference.Configuration;

            if (!configuration.TryGetValue("Server", out var server))
                throw new ArgumentException("The 'Server' parameter is missing in the extension reference.");

            if (!configuration.TryGetValue("ProjectPath", out var projectPath))
                throw new ArgumentException("The 'ProjectPath' parameter is missing in the extension reference.");

            if (!configuration.TryGetValue("Package", out var package))
                throw new ArgumentException("The 'Package' parameter is missing in the extension reference.");

            configuration.TryGetValue("Token", out var token);

            var headers = new Dictionary<string, string>();

            if (!string.IsNullOrWhiteSpace(token))
                headers["PRIVATE-TOKEN"] = token;

            await foreach (var gitlabPackage in GetGitLabPackagesGenericAsync(server, projectPath, package, headers, cancellationToken))
            {
                var packageVersion = gitlabPackage.GetProperty("version").GetString() ?? throw new Exception("version is null");
                result.Add(packageVersion);
                _logger.LogDebug("Discovered package version {PackageVersion}", packageVersion);
            }

            result.Reverse();

            return result.ToArray();
        }

        private async Task<string> RestoreGitLabPackagesGenericAsync(string restoreRoot, CancellationToken cancellationToken)
        {
            var configuration = PackageReference.Configuration;

            if (!configuration.TryGetValue("Server", out var server))
                throw new ArgumentException("The 'Server' parameter is missing in the extension reference.");

            if (!configuration.TryGetValue("ProjectPath", out var projectPath))
                throw new ArgumentException("The 'ProjectPath' parameter is missing in the extension reference.");

            if (!configuration.TryGetValue("Package", out var package))
                throw new ArgumentException("The 'Package' parameter is missing in the extension reference.");

            if (!configuration.TryGetValue("Version", out var version))
                throw new ArgumentException("The 'Version' parameter is missing in the extension reference.");

            if (!configuration.TryGetValue("AssetSelector", out var assetSelector))
                throw new ArgumentException("The 'AssetSelector' parameter is missing in the extension reference.");

            configuration.TryGetValue("Token", out var token);

            var targetPath = Path.Combine(restoreRoot, projectPath.Replace('/', '_').ToLower(), version);

            if (!Directory.Exists(targetPath) || !Directory.EnumerateFileSystemEntries(targetPath).Any())
            {
                var headers = new Dictionary<string, string>();

                if (!string.IsNullOrWhiteSpace(token))
                    headers["PRIVATE-TOKEN"] = token;

                // get package id
                var gitlabPackage = default(JsonElement);

                await foreach (var currentPackage in PackageController
                    .GetGitLabPackagesGenericAsync(server, projectPath, package, headers, cancellationToken))
                {
                    var packageVersion = currentPackage.GetProperty("version").GetString();

                    if (packageVersion == version)
                        gitlabPackage = currentPackage;
                }

                if (gitlabPackage.ValueKind == JsonValueKind.Undefined)
                    throw new Exception("The specified version could not be found.");

                var packageId = gitlabPackage.GetProperty("id").GetInt32();

                // list package files (https://docs.gitlab.com/ee/api/packages.html#list-package-files)
                var encodedProjectPath = WebUtility.UrlEncode(projectPath);
                var requestUrl = $"{server}/api/v4/projects/{encodedProjectPath}/packages/{packageId}/package_files";

                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

                foreach (var entry in headers)
                {
                    request.Headers.Add(entry.Key, entry.Value);
                }

                using var response = await _httpClient.SendAsync(request, cancellationToken);

                response.EnsureSuccessStatusCode();

                var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var jsonDocument = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);

                // find asset
                var asset = jsonDocument.RootElement.EnumerateArray()
                    .FirstOrDefault(current => Regex.IsMatch(current.GetProperty("file_name").GetString() ?? throw new Exception("file_name is null"), assetSelector));

                var fileName = asset.GetProperty("file_name").GetString() ?? throw new Exception("file_name is null");

                if (asset.ValueKind != JsonValueKind.Undefined)
                {
                    // download package file (https://docs.gitlab.com/ee/user/packages/generic_packages/index.html#download-package-file)
                    var assetUrl = $"{server}/api/v4/projects/{encodedProjectPath}/packages/generic/{package}/{version}/{fileName}";
                    _logger.LogDebug("Restore package from source {Source} to {Target}", assetUrl, targetPath);
                    await DownloadAndExtractAsync(fileName, assetUrl, targetPath, headers);
                }
                else
                {
                    throw new Exception("No matching assets found.");
                }
            }
            else
            {
                _logger.LogDebug("Package is already restored");
            }

            return targetPath;
        }

        private static async IAsyncEnumerable<JsonElement> GetGitLabPackagesGenericAsync(
            string server, string projectPath, string package, Dictionary<string, string> headers, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // list packages (https://docs.gitlab.com/ee/api/packages.html#within-a-project)
            var encodedProjectPath = WebUtility.UrlEncode(projectPath);
            var requestUrl = $"{server}/api/v4/projects/{encodedProjectPath}/packages?package_type=generic&package_name={package}&per_page={PER_PAGE}&page={1}";

            for (int i = 0; i < MAX_PAGES; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

                foreach (var entry in headers)
                {
                    request.Headers.Add(entry.Key, entry.Value);
                }

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var message = await response.Content.ReadAsStringAsync(cancellationToken);

                var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var jsonDocument = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);

                foreach (var gitlabPackage in jsonDocument.RootElement.EnumerateArray())
                {
                    yield return gitlabPackage;
                }

                // look for more pages
                response.Headers.TryGetValues("Link", out var links);

                if (links is null)
                    throw new Exception("link is null");

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

        #endregion

        #region gitlab-releases-v4

        /* The GitLab Releases approach does work until trying to download the previously uploaded file.
         * GitLab allows only cookie-based downloads, tokens are not supported. Probably to stop the
         * exact intentation to download data in an automated way. */

        //private async Task<Dictionary<SemanticVersion, string>> DiscoverGitLabReleasesAsync(CancellationToken cancellationToken)
        //{
        //    var result = new Dictionary<SemanticVersion, string>();

        //    if (!_packageReference.TryGetValue("Server", out var server))
        //        throw new ArgumentException("The 'Server' parameter is missing in the extension reference.");

        //    if (!_packageReference.TryGetValue("ProjectPath", out var projectPath))
        //        throw new ArgumentException("The 'ProjectPath' parameter is missing in the extension reference.");

        //    var encodedProjectPath = WebUtility.UrlEncode(projectPath);
        //    var requestUrl = $"{server}/api/v4/projects/{encodedProjectPath}/releases?per_page={PER_PAGE}&page={1}";

        //    for (int i = 0; i < MAX_PAGES; i++)
        //    {
        //        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

        //        if (_packageReference.TryGetValue("Token", out var token))
        //            request.Headers.Add("PRIVATE-TOKEN", token);

        //        using var response = await _httpClient.SendAsync(request);

        //        response.EnsureSuccessStatusCode();

        //        var contentStream = await response.Content.ReadAsStreamAsync();
        //        var jsonDocument = await JsonDocument.ParseAsync(contentStream);

        //        foreach (var gitlabRelease in jsonDocument.RootElement.EnumerateArray())
        //        {
        //            var releaseTagName = gitlabRelease.GetProperty("tag_name").GetString();

        //            var isSemanticVersion = PackageLoadContext
        //                .TryParseWithPrefix(releaseTagName, out var semanticVersion);

        //            if (isSemanticVersion)
        //                result[semanticVersion] = releaseTagName;

        //            _logger.LogDebug("Discovered package version {PackageVersion}", releaseTagName);
        //        }

        //        // look for more pages
        //        response.Headers.TryGetValues("Link", out var links);

        //        if (!links.Any())
        //            break;

        //        requestUrl = links
        //            .First()
        //            .Split(",")
        //            .Where(current => current.Contains("rel=\"next\""))
        //            .Select(current => Regex.Match(current, @"\<(https:.*)\>; rel=""next""").Groups[1].Value)
        //            .FirstOrDefault();

        //        if (requestUrl == default)
        //            break;

        //        continue;
        //    }

        //    return result;
        //}

        //private async Task<string> RestoreGitLabReleasesAsync(string restoreRoot, CancellationToken cancellationToken)
        //{
        //    if (!_packageReference.TryGetValue("Server", out var server))
        //        throw new ArgumentException("The 'Server' parameter is missing in the extension reference.");

        //    if (!_packageReference.TryGetValue("ProjectPath", out var projectPath))
        //        throw new ArgumentException("The 'ProjectPath' parameter is missing in the extension reference.");

        //    if (!_packageReference.TryGetValue("Tag", out var tag))
        //        throw new ArgumentException("The 'Tag' parameter is missing in the extension reference.");

        //    if (!_packageReference.TryGetValue("AssetSelector", out var assetSelector))
        //        throw new ArgumentException("The 'AssetSelector' parameter is missing in the extension reference.");

        //    var targetPath = Path.Combine(restoreRoot, projectPath.Replace('/', '_').ToLower(), tag);

        //    if (!Directory.Exists(targetPath) || Directory.EnumerateFileSystemEntries(targetPath).Any())
        //    {
        //        var encodedProjectPath = WebUtility.UrlEncode(projectPath);
        //        var requestUrl = $"{server}/api/v4/projects/{encodedProjectPath}/releases/{tag}";

        //        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

        //        if (_packageReference.TryGetValue("Token", out var token))
        //            request.Headers.Add("PRIVATE-TOKEN", token);

        //        using var response = await _httpClient.SendAsync(request);

        //        response.EnsureSuccessStatusCode();

        //        var contentStream = await response.Content.ReadAsStreamAsync();
        //        var jsonDocument = await JsonDocument.ParseAsync(contentStream);

        //        // find asset
        //        var gitHubRelease = jsonDocument.RootElement;
        //        var releaseTagName = gitHubRelease.GetProperty("tag_name").GetString();

        //        var isSemanticVersion = PackageLoadContext
        //            .TryParseWithPrefix(releaseTagName, out var semanticVersion);

        //        var asset = gitHubRelease
        //            .GetProperty("assets").GetProperty("links")
        //            .EnumerateArray()
        //            .FirstOrDefault(current => Regex.IsMatch(current.GetProperty("name").GetString(), assetSelector));

        //        if (asset.ValueKind != JsonValueKind.Undefined)
        //        {
        //            var assetUrl = new Uri(asset.GetProperty("direct_asset_url").GetString());
        //            _logger.LogDebug("Restore package from source {Source}", assetUrl);
        //            await DownloadAndExtractAsync(fileName, assetUrl, targetPath, headers, cancellationToken);
        //        }
        //        else
        //        {
        //            throw new Exception("No matching assets found.");
        //        }
        //    }
        //    else
        //    {
        //        _logger.LogDebug("Package is already restored");
        //    }
        //
        //    return targetPath;
        //}

        #endregion

    }
}
