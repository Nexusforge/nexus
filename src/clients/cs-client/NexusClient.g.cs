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

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nexus.Client;

/// <summary>
/// A client for the Nexus system.
/// </summary>
public interface INexusClient
{
    /// <summary>
    /// Gets the <see cref="IArtifactsClient"/>.
    /// </summary>
    IArtifactsClient Artifacts { get; set; }

    /// <summary>
    /// Gets the <see cref="ICatalogsClient"/>.
    /// </summary>
    ICatalogsClient Catalogs { get; set; }

    /// <summary>
    /// Gets the <see cref="IDataClient"/>.
    /// </summary>
    IDataClient Data { get; set; }

    /// <summary>
    /// Gets the <see cref="IJobsClient"/>.
    /// </summary>
    IJobsClient Jobs { get; set; }

    /// <summary>
    /// Gets the <see cref="IPackageReferencesClient"/>.
    /// </summary>
    IPackageReferencesClient PackageReferences { get; set; }

    /// <summary>
    /// Gets the <see cref="ISourcesClient"/>.
    /// </summary>
    ISourcesClient Sources { get; set; }

    /// <summary>
    /// Gets the <see cref="IUsersClient"/>.
    /// </summary>
    IUsersClient Users { get; set; }

    /// <summary>
    /// Gets the <see cref="IWritersClient"/>.
    /// </summary>
    IWritersClient Writers { get; set; }



    /// <summary>
    /// Signs in the user.
    /// </summary>
    /// <param name="tokenPair">A pair of access and refresh tokens.</param>
    /// <returns>A task.</returns>
    void SignIn(TokenPair tokenPair);

    /// <summary>
    /// Attaches configuration data to subsequent Nexus API requests.
    /// </summary>
    /// <param name="configuration">The configuration data.</param>
    IDisposable AttachConfiguration(IDictionary<string, string> configuration);

    /// <summary>
    /// Clears configuration data for all subsequent Nexus API requests.
    /// </summary>
    void ClearConfiguration();
}

/// <inheritdoc />
public class NexusClient
{
    private const string NexusConfigurationHeaderKey = "Nexus-Configuration";
    private const string AuthorizationHeaderKey = "Authorization";

    private static string _tokenFolderPath = Path.Combine(Path.GetTempPath(), "nexus", "tokens");
    private static JsonSerializerOptions _options;

    private TokenPair? _tokenPair;
    private HttpClient _httpClient;
    private string? _tokenFilePath;

    private ArtifactsClient _artifacts;
    private CatalogsClient _catalogs;
    private DataClient _data;
    private JobsClient _jobs;
    private PackageReferencesClient _packageReferences;
    private SourcesClient _sources;
    private UsersClient _users;
    private WritersClient _writers;

    static NexusClient()
    {
        _options = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        _options.Converters.Add(new JsonStringEnumConverter());
    }

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
    public IUsersClient Users => _users;

    /// <inheritdoc />
    public IWritersClient Writers => _writers;



    /// <inheritdoc />
    public void SignIn(TokenPair tokenPair)
    {
        _tokenFilePath = Path.Combine(_tokenFolderPath, Uri.EscapeDataString(tokenPair.RefreshToken) + ".json");
        
        if (File.Exists(_tokenFilePath))
        {
            tokenPair = JsonSerializer.Deserialize<TokenPair>(File.ReadAllText(_tokenFilePath), _options)
                ?? throw new Exception($"Unable to deserialize file {_tokenFilePath} into a token pair.");
        }

        else
        {
            Directory.CreateDirectory(_tokenFolderPath);
            File.WriteAllText(_tokenFilePath, JsonSerializer.Serialize(tokenPair, _options));
        }

        _httpClient.DefaultRequestHeaders.Add(AuthorizationHeaderKey, $"Bearer {tokenPair.AccessToken}");

        _tokenPair = tokenPair;
    }

    /// <inheritdoc />
    public IDisposable AttachConfiguration(IDictionary<string, string> configuration)
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

    internal async Task<T> InvokeAsync<T>(string method, string relativeUrl, string? acceptHeaderValue, object? content, CancellationToken cancellationToken)
    {
        // prepare request
        var httpContent = content is null
            ? default
            : JsonContent.Create(content, options: _options);

        using var request = BuildRequestMessage(method, relativeUrl, httpContent, acceptHeaderValue);

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
                        using var newRequest = BuildRequestMessage(method, relativeUrl, httpContent, acceptHeaderValue);

                        try
                        {
                            var newResponse = await RefreshTokenAsync(response, newRequest, cancellationToken);

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
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            if (typeof(T) == typeof(object))
            {
                return default!;
            }

            else if (typeof(T) == typeof(StreamResponse))
            {
                return (T)(object)(new StreamResponse(response, stream));
            }

            else
            {
                var returnValue = await JsonSerializer.DeserializeAsync<T>(stream, _options);

                if (returnValue is null)
                    throw new NexusException($"N01", "Response data could not be deserialized.");

                return returnValue;
            }
        }
        finally
        {
            if (typeof(T) != typeof(StreamResponse))
                response.Dispose();
        }
    }
    
    private HttpRequestMessage BuildRequestMessage(string method, string relativeUrl, HttpContent? httpContent, string? acceptHeaderValue)
    {
        var requestMessage = new HttpRequestMessage()
        {
            Method = new HttpMethod(method),
            RequestUri = new Uri(relativeUrl, UriKind.Relative),
            Content = httpContent
        };

        if (acceptHeaderValue is not null)
            requestMessage.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(acceptHeaderValue));

        return requestMessage;
    }

    private async Task<HttpResponseMessage?> RefreshTokenAsync(
        HttpResponseMessage response, 
        HttpRequestMessage newRequest,
        CancellationToken cancellationToken)
    {
        // see https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/dev/src/Microsoft.IdentityModel.Tokens/Validators.cs#L390

        if (_tokenPair is null || response.RequestMessage is null)
            throw new Exception("Refresh token or request message is null. This should never happen.");

        var refreshRequest = new RefreshTokenRequest(RefreshToken: _tokenPair.RefreshToken);
        var tokenPair = await Users.RefreshTokenAsync(refreshRequest);

        if (_tokenFilePath is not null)
        {
            Directory.CreateDirectory(_tokenFolderPath);
            File.WriteAllText(_tokenFilePath, JsonSerializer.Serialize(tokenPair, _options));
        }

        var authorizationHeaderValue = $"Bearer {tokenPair.AccessToken}";
        _httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
        _httpClient.DefaultRequestHeaders.Add(AuthorizationHeaderKey, authorizationHeaderValue);

        _tokenPair = tokenPair;

        return await _httpClient.SendAsync(newRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private void SignOut()
    {
        _httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
        _tokenPair = null;
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
    Task<StreamResponse> DownloadArtifactAsync(string artifactId, CancellationToken cancellationToken = default);

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
    public Task<StreamResponse> DownloadArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/artifacts/{artifactId}");
        urlBuilder.Replace("{artifactId}", Uri.EscapeDataString(Convert.ToString(artifactId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("GET", url, "application/octet-stream", default, cancellationToken);
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
    Task<ResourceCatalog> GetCatalogAsync(string catalogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of child catalog identifiers for the provided parent catalog identifier.
    /// </summary>
    /// <param name="catalogId">The parent catalog identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<ICollection<string>> GetChildCatalogIdsAsync(string catalogId, CancellationToken cancellationToken = default);

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
    /// <param name="begin">Start date.</param>
    /// <param name="end">End date.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<CatalogAvailability> GetCatalogAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the specified attachment.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="attachmentId">The attachment identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> DownloadAttachementAsync(string catalogId, string attachmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the catalog metadata.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<CatalogMetadata> GetCatalogMetadataAsync(string catalogId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the catalog metadata.
    /// </summary>
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="catalogMetadata">The catalog metadata to put.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task PutCatalogMetadataAsync(string catalogId, CatalogMetadata catalogMetadata, CancellationToken cancellationToken = default);

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
    public Task<ResourceCatalog> GetCatalogAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ResourceCatalog>("GET", url, "application/json", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ICollection<string>> GetChildCatalogIdsAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}/child-catalog-ids");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ICollection<string>>("GET", url, "application/json", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CatalogTimeRange> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}/timerange");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<CatalogTimeRange>("GET", url, "application/json", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CatalogAvailability> GetCatalogAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}/availability");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));

        var queryValues = new Dictionary<string, string>()
        {
            ["begin"] = Uri.EscapeDataString(Convert.ToString(begin, CultureInfo.InvariantCulture)),
            ["end"] = Uri.EscapeDataString(Convert.ToString(end, CultureInfo.InvariantCulture)),
        };

        var query = "?" + string.Join('&', queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        urlBuilder.Append(query);

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<CatalogAvailability>("GET", url, "application/json", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> DownloadAttachementAsync(string catalogId, string attachmentId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}/attachments/{attachmentId}/content");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));
        urlBuilder.Replace("{attachmentId}", Uri.EscapeDataString(Convert.ToString(attachmentId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("GET", url, "application/octet-stream", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CatalogMetadata> GetCatalogMetadataAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}/metadata");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<CatalogMetadata>("GET", url, "application/json", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task PutCatalogMetadataAsync(string catalogId, CatalogMetadata catalogMetadata, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/catalogs/{catalogId}/metadata");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<object>("PUT", url, "", catalogMetadata, cancellationToken);
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
    /// <param name="catalogId">The catalog identifier.</param>
    /// <param name="resourceId">The resource identifier.</param>
    /// <param name="representationId">The representation identifier.</param>
    /// <param name="begin">Start date/time.</param>
    /// <param name="end">End date/time.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> GetStreamAsync(string catalogId, string resourceId, string representationId, DateTime begin, DateTime end, CancellationToken cancellationToken = default);

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
    public Task<StreamResponse> GetStreamAsync(string catalogId, string resourceId, string representationId, DateTime begin, DateTime end, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/data");

        var queryValues = new Dictionary<string, string>()
        {
            ["catalogId"] = Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)),
            ["resourceId"] = Uri.EscapeDataString(Convert.ToString(resourceId, CultureInfo.InvariantCulture)),
            ["representationId"] = Uri.EscapeDataString(Convert.ToString(representationId, CultureInfo.InvariantCulture)),
            ["begin"] = Uri.EscapeDataString(Convert.ToString(begin, CultureInfo.InvariantCulture)),
            ["end"] = Uri.EscapeDataString(Convert.ToString(end, CultureInfo.InvariantCulture)),
        };

        var query = "?" + string.Join('&', queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        urlBuilder.Append(query);

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("GET", url, "application/octet-stream", default, cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with jobs.
/// </summary>
public interface IJobsClient
{
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
    /// Gets a list of jobs.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<ICollection<Job>> GetJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of the specified job.
    /// </summary>
    /// <param name="jobId"></param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<JobStatus> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the specified job.
    /// </summary>
    /// <param name="jobId"></param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> DeleteJobAsync(Guid jobId, CancellationToken cancellationToken = default);

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
    public Task<Job> ExportAsync(ExportParameters parameters, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/jobs/export");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<Job>("POST", url, "application/json", parameters, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Job> LoadPackagesAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/jobs/load-packages");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<Job>("POST", url, "application/json", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ICollection<Job>> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/jobs");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ICollection<Job>>("GET", url, "application/json", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<JobStatus> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/jobs/{jobId}/status");
        urlBuilder.Replace("{jobId}", Uri.EscapeDataString(Convert.ToString(jobId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<JobStatus>("GET", url, "application/json", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> DeleteJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/jobs/{jobId}");
        urlBuilder.Replace("{jobId}", Uri.EscapeDataString(Convert.ToString(jobId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("DELETE", url, "application/octet-stream", default, cancellationToken);
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
    Task<IDictionary<string, PackageReference>> GetPackageReferencesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a package reference.
    /// </summary>
    /// <param name="packageReferenceId">The identifier of the package reference.</param>
    /// <param name="packageReference">The package reference to put.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task PutPackageReferencesAsync(Guid packageReferenceId, PackageReference packageReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a package reference.
    /// </summary>
    /// <param name="packageReferenceId">The ID of the package reference.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task DeletePackageReferencesAsync(Guid packageReferenceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets package versions.
    /// </summary>
    /// <param name="packageReferenceId">The ID of the package reference.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<ICollection<string>> GetPackageVersionsAsync(Guid packageReferenceId, CancellationToken cancellationToken = default);

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
    public Task<IDictionary<string, PackageReference>> GetPackageReferencesAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/packagereferences");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<IDictionary<string, PackageReference>>("GET", url, "application/json", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task PutPackageReferencesAsync(Guid packageReferenceId, PackageReference packageReference, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/packagereferences/{packageReferenceId}");
        urlBuilder.Replace("{packageReferenceId}", Uri.EscapeDataString(Convert.ToString(packageReferenceId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<object>("PUT", url, "", packageReference, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeletePackageReferencesAsync(Guid packageReferenceId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/packagereferences/{packageReferenceId}");
        urlBuilder.Replace("{packageReferenceId}", Uri.EscapeDataString(Convert.ToString(packageReferenceId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<object>("DELETE", url, "", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ICollection<string>> GetPackageVersionsAsync(Guid packageReferenceId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/packagereferences/{packageReferenceId}/versions");
        urlBuilder.Replace("{packageReferenceId}", Uri.EscapeDataString(Convert.ToString(packageReferenceId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ICollection<string>>("GET", url, "application/json", default, cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with sources.
/// </summary>
public interface ISourcesClient
{
    /// <summary>
    /// Gets the list of sources.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<ICollection<ExtensionDescription>> GetSourceDescriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of backend sources.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<IDictionary<string, DataSourceRegistration>> GetSourceRegistrationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a backend source.
    /// </summary>
    /// <param name="registrationId">The identifier of the registration.</param>
    /// <param name="registration">The registration to put.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task PutSourceRegistrationAsync(Guid registrationId, DataSourceRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a backend source.
    /// </summary>
    /// <param name="registrationId">The identifier of the registration.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task DeleteSourceRegistrationAsync(Guid registrationId, CancellationToken cancellationToken = default);

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
    public Task<ICollection<ExtensionDescription>> GetSourceDescriptionsAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/sources/descriptions");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ICollection<ExtensionDescription>>("GET", url, "application/json", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IDictionary<string, DataSourceRegistration>> GetSourceRegistrationsAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/sources/registrations");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<IDictionary<string, DataSourceRegistration>>("GET", url, "application/json", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task PutSourceRegistrationAsync(Guid registrationId, DataSourceRegistration registration, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/sources/registrations/{registrationId}");
        urlBuilder.Replace("{registrationId}", Uri.EscapeDataString(Convert.ToString(registrationId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<object>("PUT", url, "", registration, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteSourceRegistrationAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/sources/registrations/{registrationId}");
        urlBuilder.Replace("{registrationId}", Uri.EscapeDataString(Convert.ToString(registrationId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<object>("DELETE", url, "", default, cancellationToken);
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
    Task<ICollection<AuthenticationSchemeDescription>> GetAuthenticationSchemesAsync(CancellationToken cancellationToken = default);

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
    /// Generates a set of tokens.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<TokenPair> GenerateTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of users.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<ICollection<NexusUser>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a claim.
    /// </summary>
    /// <param name="userId">The identifier of the user.</param>
    /// <param name="claimId">The identifier of claim.</param>
    /// <param name="claim">The claim to put.</param>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<StreamResponse> PutClaimAsync(string userId, Guid claimId, NexusClaim claim, CancellationToken cancellationToken = default);

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
    public Task<ICollection<AuthenticationSchemeDescription>> GetAuthenticationSchemesAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/authentication-schemes");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ICollection<AuthenticationSchemeDescription>>("GET", url, "application/json", default, cancellationToken);
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
        return _client.InvokeAsync<StreamResponse>("GET", url, "application/octet-stream", default, cancellationToken);
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
        return _client.InvokeAsync<StreamResponse>("GET", url, "application/octet-stream", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TokenPair> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/refresh-token");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<TokenPair>("POST", url, "application/json", request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> RevokeTokenAsync(RevokeTokenRequest request, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/revoke-token");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("POST", url, "application/octet-stream", request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<NexusUser> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/me");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<NexusUser>("GET", url, "application/json", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TokenPair> GenerateTokensAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/generate-tokens");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<TokenPair>("POST", url, "application/json", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ICollection<NexusUser>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ICollection<NexusUser>>("GET", url, "application/json", default, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> PutClaimAsync(string userId, Guid claimId, NexusClaim claim, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/{userId}/{claimId}");
        urlBuilder.Replace("{userId}", Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!));
        urlBuilder.Replace("{claimId}", Uri.EscapeDataString(Convert.ToString(claimId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("PUT", url, "application/octet-stream", claim, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StreamResponse> DeleteClaimAsync(string userId, Guid claimId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/users/{userId}/{claimId}");
        urlBuilder.Replace("{userId}", Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)!));
        urlBuilder.Replace("{claimId}", Uri.EscapeDataString(Convert.ToString(claimId, CultureInfo.InvariantCulture)!));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("DELETE", url, "application/octet-stream", default, cancellationToken);
    }

}

/// <summary>
/// Provides methods to interact with writers.
/// </summary>
public interface IWritersClient
{
    /// <summary>
    /// Gets the list of writers.
    /// </summary>
    /// <param name="cancellationToken">The token to cancel the current operation.</param>
    Task<ICollection<ExtensionDescription>> GetWriterDescriptionsAsync(CancellationToken cancellationToken = default);

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
    public Task<ICollection<ExtensionDescription>> GetWriterDescriptionsAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/writers/descriptions");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ICollection<ExtensionDescription>>("GET", url, "application/json", default, cancellationToken);
    }

}

/// <summary>
/// A stream response. 
/// </summary>
public class StreamResponse : IDisposable
{
    HttpResponseMessage _response;

    internal StreamResponse(HttpResponseMessage response, Stream stream)
    {
        _response = response;

        Stream = stream;
    }

    /// <summary>
    /// The stream.
    /// </summary>
    public Stream Stream { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Stream.Dispose();
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
/// <param name="Properties">Gets the map of properties.</param>
/// <param name="Resources">Gets the list of representations.</param>
public record ResourceCatalog(string Id, IDictionary<string, string>? Properties, ICollection<Resource>? Resources);

/// <summary>
/// A resource is part of a resource catalog and holds a list of representations.
/// </summary>
/// <param name="Id">Gets the identifier.</param>
/// <param name="Properties">Gets the map of properties.</param>
/// <param name="Representations">Gets the list of representations.</param>
public record Resource(string Id, IDictionary<string, string>? Properties, ICollection<Representation>? Representations);

/// <summary>
/// A representation is part of a resource.
/// </summary>
/// <param name="DataType">Gets the data type.</param>
/// <param name="SamplePeriod">Gets the sample period.</param>
/// <param name="Detail">Gets the detail.</param>
/// <param name="IsPrimary">Gets a value which indicates the primary representation to be used for aggregations. The value of this property is only relevant for resources with multiple representations.</param>
public record Representation(NexusDataType DataType, TimeSpan SamplePeriod, string? Detail, bool IsPrimary);

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
/// A catalog time range.
/// </summary>
/// <param name="Begin">The date/time of the first data in the catalog.</param>
/// <param name="End">The date/time of the last data in the catalog.</param>
public record CatalogTimeRange(DateTime Begin, DateTime End);

/// <summary>
/// The catalog availability.
/// </summary>
/// <param name="Data">The actual availability data.</param>
public record CatalogAvailability(IDictionary<string, double> Data);

/// <summary>
/// A structure for catalog metadata.
/// </summary>
/// <param name="Contact">The contact.</param>
/// <param name="IsHidden">A boolean which indicates if the catalog should be hidden.</param>
/// <param name="GroupMemberships">A list of groups the catalog is part of.</param>
/// <param name="Overrides">Overrides for the catalog.</param>
public record CatalogMetadata(string? Contact, bool IsHidden, ICollection<string>? GroupMemberships, ResourceCatalog? Overrides);

/// <summary>
/// Description of a job.
/// </summary>
/// <param name="Id">06f8eb30-5924-4a71-bdff-322f92343f5b</param>
/// <param name="Type">export</param>
/// <param name="Owner">test@nexus.localhost</param>
/// <param name="Parameters">Job parameters.</param>
public record Job(Guid Id, string Type, string Owner, object? Parameters);

/// <summary>
/// A structure for export parameters.
/// </summary>
/// <param name="Begin">2020-02-01T00:00:00Z</param>
/// <param name="End">2020-02-02T00:00:00Z</param>
/// <param name="FilePeriod">00:00:00</param>
/// <param name="Type">Nexus.Writers.Csv</param>
/// <param name="ResourcePaths">["/IN_MEMORY/TEST/ACCESSIBLE/T1/1_s_mean", "/IN_MEMORY/TEST/ACCESSIBLE/V1/1_s_mean"]</param>
/// <param name="Configuration">{ "RowIndexFormat": "Index", "SignificantFigures": "4" }</param>
public record ExportParameters(DateTime Begin, DateTime End, TimeSpan FilePeriod, string Type, ICollection<string> ResourcePaths, IDictionary<string, string> Configuration);

/// <summary>
/// Describes the status of the job.
/// </summary>
/// <param name="Start">The start date/time.</param>
/// <param name="Status">The status.</param>
/// <param name="Progress">The progress from 0 to 1.</param>
/// <param name="ExceptionMessage">An optional exception message.</param>
/// <param name="Result">The optional result.</param>
public record JobStatus(DateTime Start, TaskStatus Status, double Progress, string? ExceptionMessage, object? Result);

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
/// A package reference.
/// </summary>
/// <param name="Provider">The provider which loads the package.</param>
/// <param name="Configuration">The configuration of the package reference.</param>
public record PackageReference(string Provider, IDictionary<string, string> Configuration);

/// <summary>
/// An extension description.
/// </summary>
/// <param name="Type">The extension type.</param>
/// <param name="Description">An optional description.</param>
public record ExtensionDescription(string Type, string? Description);

/// <summary>
/// A backend source.
/// </summary>
/// <param name="Type">The type of the backend source.</param>
/// <param name="ResourceLocator">An URL which points to the data.</param>
/// <param name="Configuration">Configuration parameters for the instantiated source.</param>
/// <param name="Publish">A boolean which indicates if the found catalogs should be available for everyone.</param>
/// <param name="Disable">A boolean which indicates if this backend source should be ignored.</param>
public record DataSourceRegistration(string Type, Uri ResourceLocator, IDictionary<string, string> Configuration, bool Publish, bool Disable);

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
public record NexusUser(string Id, string Name, ICollection<RefreshToken> RefreshTokens, IDictionary<string, NexusClaim> Claims);

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

