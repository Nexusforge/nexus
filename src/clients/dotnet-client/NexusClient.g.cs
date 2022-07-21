#nullable enable

// 0 = Namespace
// 1 = ClientName
// 2 = NexusConfigurationHeaderKey
// 3 = AuthorizationHeaderKey
// 4 = SubClientFields
// 5 = SubClientFieldAssignment
// 6 = SubClientProperties
// 7 = SubClientSource
// 8 = ExceptionType
// 9 = Models
// 10 = SubClientInterfaceProperties

using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Api;

/// <summary>
/// A client for the Nexus system.
/// </summary>
public interface INexusClient
{
    /// <summary>
    /// Gets the <see cref="IArtifactsClient"/>.
    /// </summary>
    IArtifactsClient Artifacts { get; }

    /// <summary>
    /// Gets the <see cref="ICatalogsClient"/>.
    /// </summary>
    ICatalogsClient Catalogs { get; }

    /// <summary>
    /// Gets the <see cref="IDataClient"/>.
    /// </summary>
    IDataClient Data { get; }

    /// <summary>
    /// Gets the <see cref="IJobsClient"/>.
    /// </summary>
    IJobsClient Jobs { get; }

    /// <summary>
    /// Gets the <see cref="IPackageReferencesClient"/>.
    /// </summary>
    IPackageReferencesClient PackageReferences { get; }

    /// <summary>
    /// Gets the <see cref="ISourcesClient"/>.
    /// </summary>
    ISourcesClient Sources { get; }

    /// <summary>
    /// Gets the <see cref="ISystemClient"/>.
    /// </summary>
    ISystemClient System { get; }

    /// <summary>
    /// Gets the <see cref="IUsersClient"/>.
    /// </summary>
    IUsersClient Users { get; }

    /// <summary>
    /// Gets the <see cref="IWritersClient"/>.
    /// </summary>
    IWritersClient Writers { get; }



    /// <summary>
    /// Signs in the user.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    /// <returns>A task.</returns>
    Task SignInAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>
    /// Attaches configuration data to subsequent Nexus API requests.
    /// </summary>
    /// <param name="configuration">The configuration data.</param>
    IDisposable AttachConfiguration(object configuration);

    /// <summary>
    /// Clears configuration data for all subsequent Nexus API requests.
    /// </summary>
    void ClearConfiguration();
}

/// <inheritdoc />
public class NexusClient : INexusClient, IDisposable
{
    private const string NexusConfigurationHeaderKey = "Nexus-Configuration";
    private const string AuthorizationHeaderKey = "Authorization";

    private static string _tokenFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexusapi", "tokens");

    private TokenPair? _tokenPair;
    private HttpClient _httpClient;
    private string? _tokenFilePath;

    private ArtifactsClient _artifacts;
    private CatalogsClient _catalogs;
    private DataClient _data;
    private JobsClient _jobs;
    private PackageReferencesClient _packageReferences;
    private SourcesClient _sources;
    private SystemClient _system;
    private UsersClient _users;
    private WritersClient _writers;

    /// <summary>
    /// Initializes a new instance of the <see cref="NexusClient"/>.
    /// </summary>
    /// <param name="baseUrl">The base URL to connect to.</param>
    public NexusClient(Uri baseUrl) : this(new HttpClient() { BaseAddress = baseUrl })
    {
        //
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NexusClient"/>.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use.</param>
    public NexusClient(HttpClient httpClient)
    {
        if (httpClient.BaseAddress is null)
            throw new Exception("The base address of the HTTP client must be set.");

        _httpClient = httpClient;

        _artifacts = new ArtifactsClient(this);
        _catalogs = new CatalogsClient(this);
        _data = new DataClient(this);
        _jobs = new JobsClient(this);
        _packageReferences = new PackageReferencesClient(this);
        _sources = new SourcesClient(this);
        _system = new SystemClient(this);
        _users = new UsersClient(this);
        _writers = new WritersClient(this);

    }

    /// <summary>
    /// Gets a value which indicates if the user is authenticated.
    /// </summary>
    public bool IsAuthenticated => _tokenPair is not null;

    /// <inheritdoc />
    public IArtifactsClient Artifacts => _artifacts;

    /// <inheritdoc />
    public ICatalogsClient Catalogs => _catalogs;

    /// <inheritdoc />
    public IDataClient Data => _data;

    /// <inheritdoc />
    public IJobsClient Jobs => _jobs;

    /// <inheritdoc />
    public IPackageReferencesClient PackageReferences => _packageReferences;

    /// <inheritdoc />
    public ISourcesClient Sources => _sources;

    /// <inheritdoc />
    public ISystemClient System => _system;

    /// <inheritdoc />
    public IUsersClient Users => _users;

    /// <inheritdoc />
    public IWritersClient Writers => _writers;



    /// <inheritdoc />
    public async Task SignInAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        string actualRefreshToken;

        _tokenFilePath = Path.Combine(_tokenFolderPath, Uri.EscapeDataString(refreshToken) + ".json");
        
        if (File.Exists(_tokenFilePath))
        {
            actualRefreshToken = JsonSerializer.Deserialize<string>(File.ReadAllText(_tokenFilePath), Utilities.JsonOptions)
                ?? throw new Exception($"Unable to deserialize file {_tokenFilePath}.");
        }

        else
        {
            Directory.CreateDirectory(_tokenFolderPath);
            File.WriteAllText(_tokenFilePath, JsonSerializer.Serialize(refreshToken, Utilities.JsonOptions));
            actualRefreshToken = refreshToken;
        }

        await RefreshTokenAsync(actualRefreshToken, cancellationToken);
    }

    /// <inheritdoc />
    public IDisposable AttachConfiguration(object configuration)
    {
        var encodedJson = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(configuration));

        _httpClient.DefaultRequestHeaders.Remove(NexusConfigurationHeaderKey);
        _httpClient.DefaultRequestHeaders.Add(NexusConfigurationHeaderKey, encodedJson);

        return new DisposableConfiguration(this);
    }

    /// <inheritdoc />
    public void ClearConfiguration()
    {
        _httpClient.DefaultRequestHeaders.Remove(NexusConfigurationHeaderKey);
    }

    internal async Task<T> InvokeAsync<T>(string method, string relativeUrl, string? acceptHeaderValue, string? contentTypeValue, HttpContent? content, CancellationToken cancellationToken)
    {
        // prepare request
        using var request = BuildRequestMessage(method, relativeUrl, content, contentTypeValue, acceptHeaderValue);

        // send request
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // process response
        if (!response.IsSuccessStatusCode)
        {
            // try to refresh the access token
            if (response.StatusCode == HttpStatusCode.Unauthorized && _tokenPair is not null)
            {
                var wwwAuthenticateHeader = response.Headers.WwwAuthenticate.FirstOrDefault();
                var signOut = true;

                if (wwwAuthenticateHeader is not null)
                {
                    var parameter = wwwAuthenticateHeader.Parameter;

                    if (parameter is not null && parameter.Contains("The token expired at"))
                    {
                        try
                        {
                            await RefreshTokenAsync(_tokenPair.RefreshToken, cancellationToken);

                            using var newRequest = BuildRequestMessage(method, relativeUrl, content, contentTypeValue, acceptHeaderValue);
                            var newResponse = await _httpClient.SendAsync(newRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                            if (newResponse is not null)
                            {
                                response.Dispose();
                                response = newResponse;
                                signOut = false;
                            }
                        }
                        catch
                        {
                            //
                        }
                    }
                }

                if (signOut)
                    SignOut();
            }

            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync();
                var statusCode = $"N00.{response.StatusCode}";

                if (string.IsNullOrWhiteSpace(message))
                    throw new NexusException(statusCode, $"The HTTP request failed with status code {response.StatusCode}.");

                else
                    throw new NexusException(statusCode, $"The HTTP request failed with status code {response.StatusCode}. The response message is: {message}");
            }
        }

        try
        {
            if (typeof(T) == typeof(object))
            {
                return default!;
            }

            else if (typeof(T) == typeof(StreamResponse))
            {
                return (T)(object)(new StreamResponse(response));
            }

            else
            {
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

                try
                {
                    return await JsonSerializer.DeserializeAsync<T>(stream, Utilities.JsonOptions);    
                }
                catch (Exception ex)
                {
                    throw new NexusException($"N01", "Response data could not be deserialized.", ex);
                }
            }
        }
        finally
        {
            if (typeof(T) != typeof(StreamResponse))
                response.Dispose();
        }
    }
    
    private static readonly HttpRequestOptionsKey<bool> WebAssemblyEnableStreamingResponseKey = new HttpRequestOptionsKey<bool>("WebAssemblyEnableStreamingResponse");

    private HttpRequestMessage BuildRequestMessage(string method, string relativeUrl, HttpContent? content, string? contentTypeHeaderValue, string? acceptHeaderValue)
    {
        var requestMessage = new HttpRequestMessage()
        {
            Method = new HttpMethod(method),
            RequestUri = new Uri(relativeUrl, UriKind.Relative),
            Content = content
        };

        if (contentTypeHeaderValue is not null && requestMessage.Content is not null)
            requestMessage.Content.Headers.ContentType = MediaTypeWithQualityHeaderValue.Parse(contentTypeHeaderValue);

        if (acceptHeaderValue is not null)
            requestMessage.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(acceptHeaderValue));

        // For web assembly
        // https://docs.microsoft.com/de-de/dotnet/api/microsoft.aspnetcore.components.webassembly.http.webassemblyhttprequestmessageextensions.setbrowserresponsestreamingenabled?view=aspnetcore-6.0
        // https://github.com/dotnet/aspnetcore/blob/0ee742c53f2669fd7233df6da89db5e8ab944585/src/Components/WebAssembly/WebAssembly/src/Http/WebAssemblyHttpRequestMessageExtensions.cs
        requestMessage.Options.Set(WebAssemblyEnableStreamingResponseKey, true);

        return requestMessage;
    }

    private async Task RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        // see https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/dev/src/Microsoft.IdentityModel.Tokens/Validators.cs#L390

        var refreshRequest = new RefreshTokenRequest(refreshToken);
        var tokenPair = await Users.RefreshTokenAsync(refreshRequest, cancellationToken);

        if (_tokenFilePath is not null)
        {
            Directory.CreateDirectory(_tokenFolderPath);
            File.WriteAllText(_tokenFilePath, JsonSerializer.Serialize(tokenPair.RefreshToken, Utilities.JsonOptions));
        }

        var authorizationHeaderValue = $"Bearer {tokenPair.AccessToken}";
        _httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
        _httpClient.DefaultRequestHeaders.Add(AuthorizationHeaderKey, authorizationHeaderValue);

        _tokenPair = tokenPair;
    }

    private void SignOut()
    {
        _httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
        _tokenPair = default;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

/// <summary>
/// Provides methods to interact with artifacts.
/// </summary>
public interface IArtifactsClient
{
    /// <summary>
    /// Gets the specified artifact.
    /// </summary>
    /// <param name="artifactId">The artifact identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> DownloadAsync(string artifactId, CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class ArtifactsClient : IArtifactsClient
{
    private NexusClient _client;
    
    internal ArtifactsClient(NexusClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public Task<StreamResponse> DownloadAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/artifacts/{artifactId}");
        urlBuilder.Replace("{artifactId}", Uri.EscapeDataString(Convert.ToString(artifactId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("GET", url, "application/octet-stream", default, default, cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with catalogs.
/// </summary>
public interface ICatalogsClient
{
    /// <summary>
    /// Gets the specified catalog.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<ResourceCatalog> GetAsync(string catalogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of child catalog info for the provided parent catalog identifier.
    /// </summary>
    /// <param name="catalogId">The parent catalog identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IList<CatalogInfo>> GetChildCatalogInfosAsync(string catalogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the specified catalog's time range.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<CatalogTimeRange> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the specified catalog availability.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="begin">Start date/time.</param>
    /// <param name="end">End date/time.</param>
    /// <param name="step">Step period.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<CatalogAvailability> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, TimeSpan step, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all attachments for the specified catalog.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IList<string>> GetAttachmentsAsync(string catalogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads the specified attachment.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="attachmentId">The attachment identifier.</param>
    /// <param name="content">The binary file content.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> UploadAttachmentAsync(string catalogId, string attachmentId, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the specified attachment.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="attachmentId">The attachment identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> DeleteAttachmentAsync(string catalogId, string attachmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the specified attachment.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="attachmentId">The attachment identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> GetAttachmentStreamAsync(string catalogId, string attachmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the catalog metadata.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<CatalogMetadata> GetMetadataAsync(string catalogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the catalog metadata.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="catalogMetadata">The catalog metadata to set.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task SetMetadataAsync(string catalogId, CatalogMetadata catalogMetadata, CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class CatalogsClient : ICatalogsClient
{
    private NexusClient _client;
    
    internal CatalogsClient(NexusClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public Task<ResourceCatalog> GetAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ResourceCatalog>("GET", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IList<CatalogInfo>> GetChildCatalogInfosAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}/child-catalog-infos");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<IList<CatalogInfo>>("GET", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CatalogTimeRange> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}/timerange");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<CatalogTimeRange>("GET", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CatalogAvailability> GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, TimeSpan step, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}/availability");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));

        var queryValues = new Dictionary<string, string>()
        {
            ["begin"] = Uri.EscapeDataString(Convert.ToString(begin, CultureInfo.InvariantCulture)),
            ["end"] = Uri.EscapeDataString(Convert.ToString(end, CultureInfo.InvariantCulture)),
            ["step"] = Uri.EscapeDataString(Convert.ToString(step, CultureInfo.InvariantCulture)),
        };

        var query = "?" + string.Join('&', queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        urlBuilder.Append(query);

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<CatalogAvailability>("GET", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IList<string>> GetAttachmentsAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}/attachments");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<IList<string>>("GET", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> UploadAttachmentAsync(string catalogId, string attachmentId, Stream content, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}/attachments/{attachmentId}");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));
        urlBuilder.Replace("{attachmentId}", Uri.EscapeDataString(Convert.ToString(attachmentId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("PUT", url, "application/octet-stream", "application/octet-stream", new StreamContent(content), cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> DeleteAttachmentAsync(string catalogId, string attachmentId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}/attachments/{attachmentId}");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));
        urlBuilder.Replace("{attachmentId}", Uri.EscapeDataString(Convert.ToString(attachmentId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("DELETE", url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> GetAttachmentStreamAsync(string catalogId, string attachmentId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}/attachments/{attachmentId}/content");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));
        urlBuilder.Replace("{attachmentId}", Uri.EscapeDataString(Convert.ToString(attachmentId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("GET", url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CatalogMetadata> GetMetadataAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}/metadata");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<CatalogMetadata>("GET", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task SetMetadataAsync(string catalogId, CatalogMetadata catalogMetadata, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}/metadata");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<object>("PUT", url, "", "application/json", JsonContent.Create(catalogMetadata, options: Utilities.JsonOptions), cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with data.
/// </summary>
public interface IDataClient
{
    /// <summary>
    /// Gets the requested data.
    /// </summary>
    /// <param name="resourcePath">The path to the resource data to stream.</param>
    /// <param name="begin">Start date/time.</param>
    /// <param name="end">End date/time.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> GetStreamAsync(string resourcePath, DateTime begin, DateTime end, CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class DataClient : IDataClient
{
    private NexusClient _client;
    
    internal DataClient(NexusClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public Task<StreamResponse> GetStreamAsync(string resourcePath, DateTime begin, DateTime end, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/data");

        var queryValues = new Dictionary<string, string>()
        {
            ["resourcePath"] = Uri.EscapeDataString(Convert.ToString(resourcePath, CultureInfo.InvariantCulture)),
            ["begin"] = Uri.EscapeDataString(Convert.ToString(begin, CultureInfo.InvariantCulture)),
            ["end"] = Uri.EscapeDataString(Convert.ToString(end, CultureInfo.InvariantCulture)),
        };

        var query = "?" + string.Join('&', queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        urlBuilder.Append(query);

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("GET", url, "application/octet-stream", default, default, cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with jobs.
/// </summary>
public interface IJobsClient
{
    /// <summary>
    /// Gets a list of jobs.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IList<Job>> GetJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the specified job.
    /// </summary>
    /// <param name="jobId"></param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of the specified job.
    /// </summary>
    /// <param name="jobId"></param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<JobStatus> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new export job.
    /// </summary>
    /// <param name="parameters">Export parameters.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<Job> ExportAsync(ExportParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new load packages job.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<Job> LoadPackagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the catalog cache for the specified period of time.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="begin">Start date/time.</param>
    /// <param name="end">End date/time.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<Job> ClearCacheAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class JobsClient : IJobsClient
{
    private NexusClient _client;
    
    internal JobsClient(NexusClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public Task<IList<Job>> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/jobs");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<IList<Job>>("GET", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/jobs/{jobId}");
        urlBuilder.Replace("{jobId}", Uri.EscapeDataString(Convert.ToString(jobId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("DELETE", url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<JobStatus> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/jobs/{jobId}/status");
        urlBuilder.Replace("{jobId}", Uri.EscapeDataString(Convert.ToString(jobId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<JobStatus>("GET", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Job> ExportAsync(ExportParameters parameters, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/jobs/export");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<Job>("POST", url, "application/json", "application/json", JsonContent.Create(parameters, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public Task<Job> LoadPackagesAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/jobs/load-packages");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<Job>("POST", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Job> ClearCacheAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/jobs/clear-cache");

        var queryValues = new Dictionary<string, string>()
        {
            ["catalogId"] = Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)),
            ["begin"] = Uri.EscapeDataString(Convert.ToString(begin, CultureInfo.InvariantCulture)),
            ["end"] = Uri.EscapeDataString(Convert.ToString(end, CultureInfo.InvariantCulture)),
        };

        var query = "?" + string.Join('&', queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        urlBuilder.Append(query);

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<Job>("POST", url, "application/json", default, default, cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with package references.
/// </summary>
public interface IPackageReferencesClient
{
    /// <summary>
    /// Gets the list of package references.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IList<PackageReference>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a package reference.
    /// </summary>
    /// <param name="packageReference">The package reference to set.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task SetAsync(PackageReference packageReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a package reference.
    /// </summary>
    /// <param name="packageReferenceId">The ID of the package reference.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task DeleteAsync(Guid packageReferenceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets package versions.
    /// </summary>
    /// <param name="packageReferenceId">The ID of the package reference.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IList<string>> GetVersionsAsync(Guid packageReferenceId, CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class PackageReferencesClient : IPackageReferencesClient
{
    private NexusClient _client;
    
    internal PackageReferencesClient(NexusClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public Task<IList<PackageReference>> GetAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/packagereferences");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<IList<PackageReference>>("GET", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task SetAsync(PackageReference packageReference, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/packagereferences");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<object>("PUT", url, "", "application/json", JsonContent.Create(packageReference, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid packageReferenceId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/packagereferences/{packageReferenceId}");
        urlBuilder.Replace("{packageReferenceId}", Uri.EscapeDataString(Convert.ToString(packageReferenceId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<object>("DELETE", url, "", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IList<string>> GetVersionsAsync(Guid packageReferenceId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/packagereferences/{packageReferenceId}/versions");
        urlBuilder.Replace("{packageReferenceId}", Uri.EscapeDataString(Convert.ToString(packageReferenceId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<IList<string>>("GET", url, "application/json", default, default, cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with sources.
/// </summary>
public interface ISourcesClient
{
    /// <summary>
    /// Gets the list of source descriptions.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IList<ExtensionDescription>> GetDescriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of backend sources.
    /// </summary>
    /// <param name="userId">The optional user identifier. If not specified, the name of the current user will be used.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IList<DataSourceRegistration>> GetRegistrationsAsync(string? userId = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a backend source.
    /// </summary>
    /// <param name="userId">The optional user identifier. If not specified, the name of the current user will be used.</param>
    /// <param name="registration">The registration to set.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> SetRegistrationAsync(DataSourceRegistration registration, string? userId = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a backend source.
    /// </summary>
    /// <param name="registrationId">The identifier of the registration.</param>
    /// <param name="userId">The optional user identifier. If not specified, the name of the current user will be used.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> DeleteRegistrationAsync(Guid registrationId, string? userId = default, CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class SourcesClient : ISourcesClient
{
    private NexusClient _client;
    
    internal SourcesClient(NexusClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public Task<IList<ExtensionDescription>> GetDescriptionsAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/sources/descriptions");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<IList<ExtensionDescription>>("GET", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IList<DataSourceRegistration>> GetRegistrationsAsync(string? userId = default, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/sources/registrations");

        var queryValues = new Dictionary<string, string>()
        {
            ["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)),
        };

        var query = "?" + string.Join('&', queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        urlBuilder.Append(query);

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<IList<DataSourceRegistration>>("GET", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> SetRegistrationAsync(DataSourceRegistration registration, string? userId = default, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/sources/registrations");

        var queryValues = new Dictionary<string, string>()
        {
            ["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)),
        };

        var query = "?" + string.Join('&', queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        urlBuilder.Append(query);

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("PUT", url, "application/octet-stream", "application/json", JsonContent.Create(registration, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> DeleteRegistrationAsync(Guid registrationId, string? userId = default, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/sources/registrations/{registrationId}");
        urlBuilder.Replace("{registrationId}", Uri.EscapeDataString(Convert.ToString(registrationId, CultureInfo.InvariantCulture)!));

        var queryValues = new Dictionary<string, string>()
        {
            ["userId"] = Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)),
        };

        var query = "?" + string.Join('&', queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        urlBuilder.Append(query);

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("DELETE", url, "application/octet-stream", default, default, cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with system.
/// </summary>
public interface ISystemClient
{
    /// <summary>
    /// Gets the configured help link.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<string> GetHelpLinkAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the system configuration.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<JsonElement?> GetConfigurationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the system configuration.
    /// </summary>
    /// <param name="configuration"></param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task SetConfigurationAsync(JsonElement configuration, CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class SystemClient : ISystemClient
{
    private NexusClient _client;
    
    internal SystemClient(NexusClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public Task<string> GetHelpLinkAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/system/help-link");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<string>("GET", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<JsonElement?> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/system/configuration");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<JsonElement?>("GET", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task SetConfigurationAsync(JsonElement configuration, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/system/configuration");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<object>("PUT", url, "", "application/json", JsonContent.Create(configuration, options: Utilities.JsonOptions), cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with users.
/// </summary>
public interface IUsersClient
{
    /// <summary>
    /// Returns a list of available authentication schemes.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IList<AuthenticationSchemeDescription>> GetAuthenticationSchemesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates the user.
    /// </summary>
    /// <param name="scheme">The authentication scheme to challenge.</param>
    /// <param name="returnUrl">The URL to return after successful authentication.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> AuthenticateAsync(string scheme, string returnUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs out the user.
    /// </summary>
    /// <param name="returnUrl"></param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> SignOutAsync(string returnUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the JWT token.
    /// </summary>
    /// <param name="request">The refresh token request.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<TokenPair> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a refresh token.
    /// </summary>
    /// <param name="request">The revoke token request.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> RevokeTokenAsync(RevokeTokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current user.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<NexusUser> GetMeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a refresh token.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<string> GenerateRefreshTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts the license of the specified catalog.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> AcceptLicenseAsync(string catalogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of users.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IList<NexusUser>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> DeleteUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a claim.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    /// <param name="claimId">The identifier of claim.</param>
    /// <param name="claim">The claim to set.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> SetClaimAsync(string userId, Guid claimId, NexusClaim claim, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a claim.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    /// <param name="claimId">The identifier of the claim.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> DeleteClaimAsync(string userId, Guid claimId, CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class UsersClient : IUsersClient
{
    private NexusClient _client;
    
    internal UsersClient(NexusClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public Task<IList<AuthenticationSchemeDescription>> GetAuthenticationSchemesAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/authentication-schemes");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<IList<AuthenticationSchemeDescription>>("GET", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> AuthenticateAsync(string scheme, string returnUrl, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/authenticate");

        var queryValues = new Dictionary<string, string>()
        {
            ["scheme"] = Uri.EscapeDataString(Convert.ToString(scheme, CultureInfo.InvariantCulture)),
            ["returnUrl"] = Uri.EscapeDataString(Convert.ToString(returnUrl, CultureInfo.InvariantCulture)),
        };

        var query = "?" + string.Join('&', queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        urlBuilder.Append(query);

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("POST", url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> SignOutAsync(string returnUrl, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/signout");

        var queryValues = new Dictionary<string, string>()
        {
            ["returnUrl"] = Uri.EscapeDataString(Convert.ToString(returnUrl, CultureInfo.InvariantCulture)),
        };

        var query = "?" + string.Join('&', queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        urlBuilder.Append(query);

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("POST", url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TokenPair> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/refresh-token");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<TokenPair>("POST", url, "application/json", "application/json", JsonContent.Create(request, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> RevokeTokenAsync(RevokeTokenRequest request, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/revoke-token");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("POST", url, "application/octet-stream", "application/json", JsonContent.Create(request, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public Task<NexusUser> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/me");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<NexusUser>("GET", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> GenerateRefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/generate-refresh-token");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<string>("POST", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> AcceptLicenseAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/accept-license");

        var queryValues = new Dictionary<string, string>()
        {
            ["catalogId"] = Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)),
        };

        var query = "?" + string.Join('&', queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        urlBuilder.Append(query);

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("GET", url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IList<NexusUser>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<IList<NexusUser>>("GET", url, "application/json", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> DeleteUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/{userId}");
        urlBuilder.Replace("{userId}", Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("DELETE", url, "application/octet-stream", default, default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> SetClaimAsync(string userId, Guid claimId, NexusClaim claim, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/{userId}/{claimId}");
        urlBuilder.Replace("{userId}", Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!));
        urlBuilder.Replace("{claimId}", Uri.EscapeDataString(Convert.ToString(claimId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("PUT", url, "application/octet-stream", "application/json", JsonContent.Create(claim, options: Utilities.JsonOptions), cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> DeleteClaimAsync(string userId, Guid claimId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/{userId}/{claimId}");
        urlBuilder.Replace("{userId}", Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!));
        urlBuilder.Replace("{claimId}", Uri.EscapeDataString(Convert.ToString(claimId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("DELETE", url, "application/octet-stream", default, default, cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with writers.
/// </summary>
public interface IWritersClient
{
    /// <summary>
    /// Gets the list of writer descriptions.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IList<ExtensionDescription>> GetDescriptionsAsync(CancellationToken cancellationToken = default);

}

/// <inheritdoc />
public class WritersClient : IWritersClient
{
    private NexusClient _client;
    
    internal WritersClient(NexusClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public Task<IList<ExtensionDescription>> GetDescriptionsAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/writers/descriptions");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<IList<ExtensionDescription>>("GET", url, "application/json", default, default, cancellationToken);
    }

}



internal class CastMemoryManager<TFrom, TTo> : MemoryManager<TTo>
     where TFrom : struct
     where TTo : struct
{
    private readonly Memory<TFrom> _from;

    public CastMemoryManager(Memory<TFrom> from) => _from = from;

    public override Span<TTo> GetSpan() => MemoryMarshal.Cast<TFrom, TTo>(_from.Span);

    protected override void Dispose(bool disposing)
    {
        //
    }

    public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

    public override void Unpin() => throw new NotSupportedException();
}

/// <summary>
/// A stream response. 
/// </summary>
public class StreamResponse : IDisposable
{
    private long _length;
    private HttpResponseMessage _response;

    internal StreamResponse(HttpResponseMessage response)
    {
        _response = response;
       
        if (_response.Content.Headers.TryGetValues("Content-Length", out var values) && 
            values.Any() && 
            int.TryParse(values.First(), out var contentLength))
        {
            _length = contentLength;
        }
        else
        {
            _length = -1;
        }
    }

    /// <summary>
    /// Reads the data as an array of doubles.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    public async Task<double[]> ReadAsDoubleAsync(CancellationToken cancellationToken = default)
    {
        if (_length < 0)
            throw new Exception("The data length is unknown.");

        if (_length % 8 != 0)
            throw new Exception("The data length is invalid.");

        var elementCount = _length / 8;
        var doubleBuffer = new double[elementCount];
        var byteBuffer = new CastMemoryManager<double, byte>(doubleBuffer).Memory;
        var stream = await _response.Content.ReadAsStreamAsync(cancellationToken);
        var remainingBuffer = byteBuffer;

        while (!remainingBuffer.IsEmpty)
        {
            var bytesRead = await stream.ReadAsync(remainingBuffer, cancellationToken);

            if (bytesRead == 0)
                throw new Exception("The stream ended early.");

            remainingBuffer = remainingBuffer.Slice(bytesRead);
        }

        return doubleBuffer;
    }

    /// <summary>
    /// Returns the underlying stream.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the current operation.</param>
    public Task<Stream> GetStreamAsync(CancellationToken cancellationToken = default)
    {
        return _response.Content.ReadAsStreamAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _response.Dispose();
    }
}

/// <summary>
/// A NexusException.
/// </summary>
public class NexusException : Exception
{
    internal NexusException(string statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    internal NexusException(string statusCode, string message, Exception innerException) : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// The exception status code.
    /// </summary>
    public string StatusCode { get; }
}

internal class DisposableConfiguration : IDisposable
{
    private NexusClient _client;

    public DisposableConfiguration(NexusClient client)
    {
        _client = client;
    }

    public void Dispose()
    {
        _client.ClearConfiguration();
    }
}

/// <summary>
/// A catalog is a top level element and holds a list of resources.
/// </summary>
/// <param name="Id">Gets the identifier.</param>
/// <param name="Properties">Gets the properties.</param>
/// <param name="Resources">Gets the list of representations.</param>
public record ResourceCatalog(string Id, JsonElement? Properties, IList<Resource>? Resources);

/// <summary>
/// A resource is part of a resource catalog and holds a list of representations.
/// </summary>
/// <param name="Id">Gets the identifier.</param>
/// <param name="Properties">Gets the properties.</param>
/// <param name="Representations">Gets the list of representations.</param>
public record Resource(string Id, JsonElement? Properties, IList<Representation>? Representations);

/// <summary>
/// A representation is part of a resource.
/// </summary>
/// <param name="DataType">The data type.</param>
/// <param name="SamplePeriod">The sample period.</param>
public record Representation(NexusDataType DataType, TimeSpan SamplePeriod);

/// <summary>
/// Specifies the Nexus data type.
/// </summary>
public enum NexusDataType
{
    /// <summary>
    /// UINT8
    /// </summary>
    UINT8,

    /// <summary>
    /// UINT16
    /// </summary>
    UINT16,

    /// <summary>
    /// UINT32
    /// </summary>
    UINT32,

    /// <summary>
    /// UINT64
    /// </summary>
    UINT64,

    /// <summary>
    /// INT8
    /// </summary>
    INT8,

    /// <summary>
    /// INT16
    /// </summary>
    INT16,

    /// <summary>
    /// INT32
    /// </summary>
    INT32,

    /// <summary>
    /// INT64
    /// </summary>
    INT64,

    /// <summary>
    /// FLOAT32
    /// </summary>
    FLOAT32,

    /// <summary>
    /// FLOAT64
    /// </summary>
    FLOAT64
}


/// <summary>
/// A structure for catalog information.
/// </summary>
/// <param name="Id">The identifier.</param>
/// <param name="Title">The title.</param>
/// <param name="Contact">A nullable contact.</param>
/// <param name="Readme">A nullable readme.</param>
/// <param name="License">A nullable license.</param>
/// <param name="IsReadable">A boolean which indicates if the catalog is accessible.</param>
/// <param name="IsWritable">A boolean which indicates if the catalog is editable.</param>
/// <param name="IsReleased">A boolean which indicates if the catalog is released.</param>
/// <param name="IsVisible">A boolean which indicates if the catalog is visible.</param>
/// <param name="IsOwner">A boolean which indicates if the catalog is owned by the current user.</param>
/// <param name="DataSourceInfoUrl">A nullable info URL of the data source.</param>
/// <param name="DataSourceType">The data source type.</param>
/// <param name="DataSourceRegistrationId">The data source registration identifier.</param>
/// <param name="PackageReferenceId">The package reference identifier.</param>
public record CatalogInfo(string Id, string Title, string? Contact, string? Readme, string? License, bool IsReadable, bool IsWritable, bool IsReleased, bool IsVisible, bool IsOwner, string? DataSourceInfoUrl, string DataSourceType, Guid DataSourceRegistrationId, Guid PackageReferenceId);

/// <summary>
/// A catalog time range.
/// </summary>
/// <param name="Begin">The date/time of the first data in the catalog.</param>
/// <param name="End">The date/time of the last data in the catalog.</param>
public record CatalogTimeRange(DateTime Begin, DateTime End);

/// <summary>
/// The catalog availability.
/// </summary>
/// <param name="Data">The actual availability data.</param>
public record CatalogAvailability(IList<double> Data);

/// <summary>
/// A structure for catalog metadata.
/// </summary>
/// <param name="Contact">The contact.</param>
/// <param name="GroupMemberships">A list of groups the catalog is part of.</param>
/// <param name="Overrides">Overrides for the catalog.</param>
public record CatalogMetadata(string? Contact, IList<string>? GroupMemberships, ResourceCatalog? Overrides);

/// <summary>
/// Description of a job.
/// </summary>
/// <param name="Id">The global unique identifier.</param>
/// <param name="Type">The job type</param>
/// <param name="Owner">The owner of the job.</param>
/// <param name="Parameters">The job parameters.</param>
public record Job(Guid Id, string Type, string Owner, JsonElement? Parameters);

/// <summary>
/// Describes the status of the job.
/// </summary>
/// <param name="Start">The start date/time.</param>
/// <param name="Status">The status.</param>
/// <param name="Progress">The progress from 0 to 1.</param>
/// <param name="ExceptionMessage">The nullable exception message.</param>
/// <param name="Result">The nullable result.</param>
public record JobStatus(DateTime Start, TaskStatus Status, double Progress, string? ExceptionMessage, JsonElement? Result);

/// <summary>
/// 
/// </summary>
public enum TaskStatus
{
    /// <summary>
    /// Created
    /// </summary>
    Created,

    /// <summary>
    /// WaitingForActivation
    /// </summary>
    WaitingForActivation,

    /// <summary>
    /// WaitingToRun
    /// </summary>
    WaitingToRun,

    /// <summary>
    /// Running
    /// </summary>
    Running,

    /// <summary>
    /// WaitingForChildrenToComplete
    /// </summary>
    WaitingForChildrenToComplete,

    /// <summary>
    /// RanToCompletion
    /// </summary>
    RanToCompletion,

    /// <summary>
    /// Canceled
    /// </summary>
    Canceled,

    /// <summary>
    /// Faulted
    /// </summary>
    Faulted
}


/// <summary>
/// A structure for export parameters.
/// </summary>
/// <param name="Begin">The start date/time.</param>
/// <param name="End">The end date/time.</param>
/// <param name="FilePeriod">The file period.</param>
/// <param name="Type">The writer type.</param>
/// <param name="ResourcePaths">The resource paths to export.</param>
/// <param name="Configuration">The configuration.</param>
public record ExportParameters(DateTime Begin, DateTime End, TimeSpan FilePeriod, string Type, IList<string> ResourcePaths, JsonElement? Configuration);

/// <summary>
/// A package reference.
/// </summary>
/// <param name="Id">The unique identifier of the package reference.</param>
/// <param name="Provider">The provider which loads the package.</param>
/// <param name="Configuration">The configuration of the package reference.</param>
public record PackageReference(Guid Id, string Provider, IDictionary<string, string> Configuration);

/// <summary>
/// An extension description.
/// </summary>
/// <param name="Type">The extension type.</param>
/// <param name="Description">A nullable description.</param>
/// <param name="ProjectUrl">A nullable project website URL.</param>
/// <param name="RepositoryUrl">A nullable source repository URL.</param>
/// <param name="AdditionalInformation">Additional information about the extension.</param>
public record ExtensionDescription(string Type, string? Description, string? ProjectUrl, string? RepositoryUrl, JsonElement AdditionalInformation);

/// <summary>
/// A data source registration.
/// </summary>
/// <param name="Id">The unique identifier of the data source registration.</param>
/// <param name="Type">The type of the data source.</param>
/// <param name="ResourceLocator">An URL which points to the data.</param>
/// <param name="Configuration">Configuration parameters for the instantiated source.</param>
/// <param name="InfoUrl">An optional info URL.</param>
/// <param name="ReleasePattern">An optional regular expressions pattern to select the catalogs to be released. By default, all catalogs will be released.</param>
/// <param name="VisibilityPattern">An optional regular expressions pattern to select the catalogs to be visible. By default, all catalogs will be visible.</param>
public record DataSourceRegistration(Guid Id, string Type, Uri ResourceLocator, JsonElement? Configuration, string? InfoUrl, string ReleasePattern, string VisibilityPattern);

/// <summary>
/// Describes an OpenID connect provider.
/// </summary>
/// <param name="Scheme">The scheme.</param>
/// <param name="DisplayName">The display name.</param>
public record AuthenticationSchemeDescription(string Scheme, string DisplayName);

/// <summary>
/// A token pair.
/// </summary>
/// <param name="AccessToken">The JWT token.</param>
/// <param name="RefreshToken">The refresh token.</param>
public record TokenPair(string AccessToken, string RefreshToken);

/// <summary>
/// A refresh token request.
/// </summary>
/// <param name="RefreshToken">The refresh token.</param>
public record RefreshTokenRequest(string RefreshToken);

/// <summary>
/// A revoke token request.
/// </summary>
/// <param name="Token">The refresh token.</param>
public record RevokeTokenRequest(string Token);

/// <summary>
/// Represents a user.
/// </summary>
/// <param name="Id">The user identifier.</param>
/// <param name="Name">The user name.</param>
/// <param name="RefreshTokens">The list of refresh tokens.</param>
/// <param name="Claims">The map of claims.</param>
public record NexusUser(string Id, string Name, IList<RefreshToken> RefreshTokens, IDictionary<string, NexusClaim> Claims);

/// <summary>
/// A refresh token.
/// </summary>
/// <param name="Token">The refresh token.</param>
/// <param name="Created">The date/time when the token was created.</param>
/// <param name="Expires">The date/time when the token expires.</param>
/// <param name="Revoked">The date/time when the token was revoked.</param>
/// <param name="ReplacedByToken">The token that replaced this one.</param>
/// <param name="IsExpired">A boolean that indicates if the token has expired.</param>
/// <param name="IsRevoked">A boolean that indicates if the token has been revoked.</param>
/// <param name="IsActive">A boolean that indicates if the token is active.</param>
public record RefreshToken(string Token, DateTime Created, DateTime Expires, DateTime? Revoked, string? ReplacedByToken, bool IsExpired, bool IsRevoked, bool IsActive);

/// <summary>
/// Represents a claim.
/// </summary>
/// <param name="Type">The claim type.</param>
/// <param name="Value">The claim value.</param>
public record NexusClaim(string Type, string Value);



internal static class Utilities
{
    internal static JsonSerializerOptions JsonOptions { get; }

    static Utilities()
    {
        JsonOptions = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }
}