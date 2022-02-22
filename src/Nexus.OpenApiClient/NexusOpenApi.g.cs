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

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security;
using System.Net;

namespace Nexus.Client;

/// <summary>
/// The OpenAPI client for the Nexus system.
/// </summary>
public class NexusOpenApiClient
{
    private static JsonSerializerOptions _options;

    private const string NexusConfigurationHeaderKey = "Nexus-Configuration";
    private const string AuthorizationHeaderKey = "Authorization";

    private Uri _baseUrl;

    private string? _jwtToken;
    private string? _refreshToken;

    private HttpClient _httpClient;

    private ArtifactsClient _Artifacts;
    private CatalogsClient _Catalogs;
    private DataClient _Data;
    private JobsClient _Jobs;
    private PackageReferencesClient _PackageReferences;
    private SourcesClient _Sources;
    private UsersClient _Users;
    private WritersClient _Writers;

    static NexusOpenApiClient()
    {
        _options = new JsonSerializerOptions()
        {
            PropertyNameCaseInsensitive = true
        };

        _options.Converters.Add(new JsonStringEnumConverter());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NexusOpenApiClient"/>.
    /// </summary>
    /// <param name="baseUrl">The base URL to connect to.</param>
    /// <param name="httpClient">An optional HTTP client.</param>
    public NexusOpenApiClient(string baseUrl, HttpClient? httpClient = null)
    {
        _baseUrl = new Uri(baseUrl);
        _httpClient = httpClient ?? new HttpClient();

        _Artifacts = new ArtifactsClient(this);
        _Catalogs = new CatalogsClient(this);
        _Data = new DataClient(this);
        _Jobs = new JobsClient(this);
        _PackageReferences = new PackageReferencesClient(this);
        _Sources = new SourcesClient(this);
        _Users = new UsersClient(this);
        _Writers = new WritersClient(this);

    }

    /// <summary>
    /// Gets a value which indicates if the user is authenticated.
    /// </summary>
    public bool IsAuthenticated => _jwtToken != null;

    public IArtifactsClient Artifacts => _Artifacts;
    public ICatalogsClient Catalogs => _Catalogs;
    public IDataClient Data => _Data;
    public IJobsClient Jobs => _Jobs;
    public IPackageReferencesClient PackageReferences => _PackageReferences;
    public ISourcesClient Sources => _Sources;
    public IUsersClient Users => _Users;
    public IWritersClient Writers => _Writers;


    ///// <summary>
    ///// Attempts to sign in the user.
    ///// </summary>
    ///// <param name="userId">The user ID.</param>
    ///// <param name="password">The user password.</param>
    ///// <returns>A task.</returns>
    ///// <exception cref="SecurityException">Thrown when the authentication fails.</exception>
    //public async Task PasswordSignInAsync(string userId, string password)
    //{
    //    var authenticateRequest = new AuthenticateRequest(UserId: userId, Password: password);
    //    var authenticateResponse = await this.Users.AuthenticateAsync(authenticateRequest);

    //    if (authenticateResponse.Error is not null)
    //        throw new SecurityException($"Unable to authenticate. Reason: {authenticateResponse.Error}");

    //    _httpClient.DefaultRequestHeaders.Add(AuthorizationHeaderKey, $"Bearer {authenticateResponse.JwtToken}");

    //    _jwtToken = authenticateResponse.JwtToken;
    //    _refreshToken = authenticateResponse.RefreshToken;
    //}

    /// <summary>
    /// Attaches configuration data to subsequent Nexus OpenAPI requests.
    /// </summary>
    /// <param name="configuration">The configuration data.</param>
    public IDisposable AttachConfiguration(IDictionary<string, string> configuration)
    {
        var encodedJson = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(configuration));

        _httpClient.DefaultRequestHeaders.Remove(NexusConfigurationHeaderKey);
        _httpClient.DefaultRequestHeaders.Add(NexusConfigurationHeaderKey, encodedJson);

        return new DisposableConfiguration(this);
    }

    /// <summary>
    /// Clears configuration data for all subsequent Nexus OpenAPI requests.
    /// </summary>
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

        using var request = this.BuildRequestMessage(method, relativeUrl, httpContent);

        if (acceptHeaderValue is not null)
            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(acceptHeaderValue));

        // send request
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // process response
        if (!response.IsSuccessStatusCode)
        {
            // try to refresh the access token
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var wwwAuthenticateHeader = response.Headers.WwwAuthenticate.FirstOrDefault();
                var signOut = true;

                if (wwwAuthenticateHeader is not null)
                {
                    var parameter = wwwAuthenticateHeader.Parameter;

                    if (parameter is not null && parameter.Contains("The token expired at"))
                    {
                        using var newRequest = this.BuildRequestMessage(method, relativeUrl, httpContent);
                        var newResponse = await this.RefreshTokenAsync(response, newRequest, cancellationToken);

                        if (newResponse is not null)
                        {
                            response.Dispose();
                            response = newResponse;
                            signOut = false;
                        }
                    }
                }

                if (signOut)
                    this.SignOut();
            }

            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(message))
                    throw new NexusApiException($"The HTTP status code is {response.StatusCode}.");

                else
                    throw new NexusApiException($"The HTTP status code is {response.StatusCode}. The response message is: {message}");
            }
        }

        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

            if (typeof(T) == typeof(object))
            {
                return default;
            }

            else if (typeof(T) == typeof(StreamResponse))
            {
                return (T)(object)(new StreamResponse(response, stream));
            }

            else
            {
                var returnValue = await JsonSerializer.DeserializeAsync<T>(stream, _options);

                if (returnValue is null)
                    throw new NexusApiException("Response data could not be deserialized.");

                return returnValue;
            }
        }
        finally
        {
            if (typeof(T) == typeof(StreamResponse))
                response.Dispose();
        }
    }
    
    private HttpRequestMessage BuildRequestMessage(string method, string relativeUrl, HttpContent? httpContent)
    {
        return new HttpRequestMessage()
        {
            Method = new HttpMethod(method),
            RequestUri = new Uri(_baseUrl, relativeUrl),
            Content = httpContent
        };
    }

    private async Task<HttpResponseMessage?> RefreshTokenAsync(
        HttpResponseMessage response, 
        HttpRequestMessage newRequest,
        CancellationToken cancellationToken)
    {
        // see https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/dev/src/Microsoft.IdentityModel.Tokens/Validators.cs#L390

        if (_refreshToken is null || response.RequestMessage is null)
            throw new Exception("Refresh token or request message is null. This should never happen.");

        var refreshRequest = new RefreshTokenRequest(RefreshToken: _refreshToken);
        var refreshResponse = await this.Users.RefreshTokenAsync(refreshRequest);

        if (refreshResponse.Error is not null)
            return default;

        var authorizationHeaderValue = $"Bearer {refreshResponse.JwtToken}";
        _httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
        _httpClient.DefaultRequestHeaders.Add(AuthorizationHeaderKey, authorizationHeaderValue);

        _jwtToken = refreshResponse.JwtToken;
        _refreshToken = refreshResponse.RefreshToken;

        return await _httpClient.SendAsync(newRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private void SignOut()
    {
        _httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
        _refreshToken = null;
        _jwtToken = null;
    }
}

public interface IArtifactsClient
{
    Task<StreamResponse> DownloadArtifactAsync(string artifactId, CancellationToken cancellationToken = default);
}

public class ArtifactsClient : IArtifactsClient
{
    private NexusOpenApiClient _client;
    
    internal ArtifactsClient(NexusOpenApiClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets the specified artifact.
    /// </summary>
    public Task<StreamResponse> DownloadArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Artifacts/{artifactId}");
        urlBuilder.Replace("{artifactId}", Uri.EscapeDataString(Convert.ToString(artifactId, CultureInfo.InvariantCulture)));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("GET", url, "application/octet-stream", default, cancellationToken);
    }

}

public interface ICatalogsClient
{
    Task<ResourceCatalog> GetCatalogAsync(string catalogId, CancellationToken cancellationToken = default);
    Task<ICollection<string>> GetChildCatalogIdsAsync(string catalogId, CancellationToken cancellationToken = default);
    Task<CatalogTimeRange> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken = default);
    Task<CatalogAvailability> GetCatalogAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken = default);
    Task<StreamResponse> DownloadAttachementAsync(string catalogId, string attachmentId, CancellationToken cancellationToken = default);
    Task<CatalogMetadata> GetCatalogMetadataAsync(string catalogId, CancellationToken cancellationToken = default);
    Task PutCatalogMetadataAsync(string catalogId, CatalogMetadata catalogMetadata, CancellationToken cancellationToken = default);
}

public class CatalogsClient : ICatalogsClient
{
    private NexusOpenApiClient _client;
    
    internal CatalogsClient(NexusOpenApiClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets the specified catalog.
    /// </summary>
    public Task<ResourceCatalog> GetCatalogAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Catalogs/{catalogId}");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ResourceCatalog>("GET", url, "application/json", default, cancellationToken);
    }

    /// <summary>
    /// Gets a list of child catalog identifiers for the provided parent catalog identifier.
    /// </summary>
    public Task<ICollection<string>> GetChildCatalogIdsAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Catalogs/{catalogId}/child-catalog-ids");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ICollection<string>>("GET", url, "application/json", default, cancellationToken);
    }

    /// <summary>
    /// Gets the specified catalog's time range.
    /// </summary>
    public Task<CatalogTimeRange> GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Catalogs/{catalogId}/timerange");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<CatalogTimeRange>("GET", url, "application/json", default, cancellationToken);
    }

    /// <summary>
    /// Gets the specified catalog availability.
    /// </summary>
    public Task<CatalogAvailability> GetCatalogAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Catalogs/{catalogId}/availability");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)));

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

    /// <summary>
    /// Gets the specified attachment.
    /// </summary>
    public Task<StreamResponse> DownloadAttachementAsync(string catalogId, string attachmentId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Catalogs/{catalogId}/attachments/{attachmentId}/content");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)));
        urlBuilder.Replace("{attachmentId}", Uri.EscapeDataString(Convert.ToString(attachmentId, CultureInfo.InvariantCulture)));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("GET", url, "application/octet-stream", default, cancellationToken);
    }

    /// <summary>
    /// Gets the catalog metadata.
    /// </summary>
    public Task<CatalogMetadata> GetCatalogMetadataAsync(string catalogId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Catalogs/{catalogId}/metadata");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<CatalogMetadata>("GET", url, "application/json", default, cancellationToken);
    }

    /// <summary>
    /// Puts the catalog metadata.
    /// </summary>
    public Task PutCatalogMetadataAsync(string catalogId, CatalogMetadata catalogMetadata, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Catalogs/{catalogId}/metadata");
        urlBuilder.Replace("{catalogId}", Uri.EscapeDataString(Convert.ToString(catalogId, CultureInfo.InvariantCulture)));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<object>("PUT", url, "", catalogMetadata, cancellationToken);
    }

}

public interface IDataClient
{
    Task<StreamResponse> GetStreamAsync(string catalogId, string resourceId, string representationId, DateTime begin, DateTime end, CancellationToken cancellationToken = default);
}

public class DataClient : IDataClient
{
    private NexusOpenApiClient _client;
    
    internal DataClient(NexusOpenApiClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets the requested data.
    /// </summary>
    public Task<StreamResponse> GetStreamAsync(string catalogId, string resourceId, string representationId, DateTime begin, DateTime end, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Data");

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

public interface IJobsClient
{
    Task<Job> ExportAsync(ExportParameters parameters, CancellationToken cancellationToken = default);
    Task<Job> LoadPackagesAsync(CancellationToken cancellationToken = default);
    Task<ICollection<Job>> GetJobsAsync(CancellationToken cancellationToken = default);
    Task<JobStatus> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<StreamResponse> DeleteJobAsync(Guid jobId, CancellationToken cancellationToken = default);
}

public class JobsClient : IJobsClient
{
    private NexusOpenApiClient _client;
    
    internal JobsClient(NexusOpenApiClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Creates a new export job.
    /// </summary>
    public Task<Job> ExportAsync(ExportParameters parameters, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Jobs/export");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<Job>("POST", url, "application/json", parameters, cancellationToken);
    }

    /// <summary>
    /// Creates a new load packages job.
    /// </summary>
    public Task<Job> LoadPackagesAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Jobs/load-packages");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<Job>("POST", url, "application/json", default, cancellationToken);
    }

    /// <summary>
    /// Gets a list of jobs.
    /// </summary>
    public Task<ICollection<Job>> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Jobs");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ICollection<Job>>("GET", url, "application/json", default, cancellationToken);
    }

    /// <summary>
    /// Gets the status of the specified job.
    /// </summary>
    public Task<JobStatus> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Jobs/{jobId}/status");
        urlBuilder.Replace("{jobId}", Uri.EscapeDataString(Convert.ToString(jobId, CultureInfo.InvariantCulture)));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<JobStatus>("GET", url, "application/json", default, cancellationToken);
    }

    /// <summary>
    /// Cancels the specified job.
    /// </summary>
    public Task<StreamResponse> DeleteJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Jobs/{jobId}");
        urlBuilder.Replace("{jobId}", Uri.EscapeDataString(Convert.ToString(jobId, CultureInfo.InvariantCulture)));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("DELETE", url, "application/octet-stream", default, cancellationToken);
    }

}

public interface IPackageReferencesClient
{
    Task<IDictionary<string, PackageReference>> GetPackageReferencesAsync(CancellationToken cancellationToken = default);
    Task PutPackageReferencesAsync(Guid packageReferenceId, PackageReference packageReference, CancellationToken cancellationToken = default);
    Task DeletePackageReferencesAsync(Guid packageReferenceId, CancellationToken cancellationToken = default);
    Task<ICollection<string>> GetPackageVersionsAsync(Guid packageReferenceId, CancellationToken cancellationToken = default);
}

public class PackageReferencesClient : IPackageReferencesClient
{
    private NexusOpenApiClient _client;
    
    internal PackageReferencesClient(NexusOpenApiClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets the list of package references.
    /// </summary>
    public Task<IDictionary<string, PackageReference>> GetPackageReferencesAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/PackageReferences");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<IDictionary<string, PackageReference>>("GET", url, "application/json", default, cancellationToken);
    }

    /// <summary>
    /// Puts a package reference.
    /// </summary>
    public Task PutPackageReferencesAsync(Guid packageReferenceId, PackageReference packageReference, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/PackageReferences/{packageReferenceId}");
        urlBuilder.Replace("{packageReferenceId}", Uri.EscapeDataString(Convert.ToString(packageReferenceId, CultureInfo.InvariantCulture)));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<object>("PUT", url, "", packageReference, cancellationToken);
    }

    /// <summary>
    /// Deletes a package reference.
    /// </summary>
    public Task DeletePackageReferencesAsync(Guid packageReferenceId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/PackageReferences/{packageReferenceId}");
        urlBuilder.Replace("{packageReferenceId}", Uri.EscapeDataString(Convert.ToString(packageReferenceId, CultureInfo.InvariantCulture)));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<object>("DELETE", url, "", default, cancellationToken);
    }

    /// <summary>
    /// Gets package versions.
    /// </summary>
    public Task<ICollection<string>> GetPackageVersionsAsync(Guid packageReferenceId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/PackageReferences/{packageReferenceId}/versions");
        urlBuilder.Replace("{packageReferenceId}", Uri.EscapeDataString(Convert.ToString(packageReferenceId, CultureInfo.InvariantCulture)));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ICollection<string>>("GET", url, "application/json", default, cancellationToken);
    }

}

public interface ISourcesClient
{
    Task<ICollection<ExtensionDescription>> GetSourceDescriptionsAsync(CancellationToken cancellationToken = default);
    Task<IDictionary<string, DataSourceRegistration>> GetSourceRegistrationsAsync(CancellationToken cancellationToken = default);
    Task PutSourceRegistrationAsync(Guid registrationId, DataSourceRegistration registration, CancellationToken cancellationToken = default);
    Task DeleteSourceRegistrationAsync(Guid registrationId, CancellationToken cancellationToken = default);
}

public class SourcesClient : ISourcesClient
{
    private NexusOpenApiClient _client;
    
    internal SourcesClient(NexusOpenApiClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets the list of sources.
    /// </summary>
    public Task<ICollection<ExtensionDescription>> GetSourceDescriptionsAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Sources/descriptions");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ICollection<ExtensionDescription>>("GET", url, "application/json", default, cancellationToken);
    }

    /// <summary>
    /// Gets the list of backend sources.
    /// </summary>
    public Task<IDictionary<string, DataSourceRegistration>> GetSourceRegistrationsAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Sources/registrations");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<IDictionary<string, DataSourceRegistration>>("GET", url, "application/json", default, cancellationToken);
    }

    /// <summary>
    /// Puts a backend source.
    /// </summary>
    public Task PutSourceRegistrationAsync(Guid registrationId, DataSourceRegistration registration, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Sources/registrations/{registrationId}");
        urlBuilder.Replace("{registrationId}", Uri.EscapeDataString(Convert.ToString(registrationId, CultureInfo.InvariantCulture)));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<object>("PUT", url, "", registration, cancellationToken);
    }

    /// <summary>
    /// Deletes a backend source.
    /// </summary>
    public Task DeleteSourceRegistrationAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Sources/registrations/{registrationId}");
        urlBuilder.Replace("{registrationId}", Uri.EscapeDataString(Convert.ToString(registrationId, CultureInfo.InvariantCulture)));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<object>("DELETE", url, "", default, cancellationToken);
    }

}

public interface IUsersClient
{
    Task<ICollection<string>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task<ICollection<AuthenticationSchemeDescription>> GetAuthenticationSchemesAsync(CancellationToken cancellationToken = default);
    Task<StreamResponse> AuthenticateAsync(string scheme, string returnUrl, CancellationToken cancellationToken = default);
    Task<StreamResponse> SignOutAsync(string returnUrl, CancellationToken cancellationToken = default);
    Task<UserInfo> GetUserInfoAsync(CancellationToken cancellationToken = default);
    Task<RefreshTokenResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task<RevokeTokenResponse> RevokeTokenAsync(RevokeTokenRequest request, CancellationToken cancellationToken = default);
    Task<ICollection<RefreshToken>> GetRefreshTokensAsync(string userId, CancellationToken cancellationToken = default);
}

public class UsersClient : IUsersClient
{
    private NexusOpenApiClient _client;
    
    internal UsersClient(NexusOpenApiClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets a list of users.
    /// </summary>
    public Task<ICollection<string>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Users");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ICollection<string>>("GET", url, "application/json", default, cancellationToken);
    }

    /// <summary>
    /// Returns a list of available authentication schemes.
    /// </summary>
    public Task<ICollection<AuthenticationSchemeDescription>> GetAuthenticationSchemesAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Users/authentication-schemes");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ICollection<AuthenticationSchemeDescription>>("GET", url, "application/json", default, cancellationToken);
    }

    /// <summary>
    /// Authenticates the user.
    /// </summary>
    public Task<StreamResponse> AuthenticateAsync(string scheme, string returnUrl, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Users/authenticate");

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

    /// <summary>
    /// Logs out the user.
    /// </summary>
    public Task<StreamResponse> SignOutAsync(string returnUrl, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Users/signout");

        var queryValues = new Dictionary<string, string>()
        {
            ["returnUrl"] = Uri.EscapeDataString(Convert.ToString(returnUrl, CultureInfo.InvariantCulture)),
        };

        var query = "?" + string.Join('&', queryValues.Select(entry => $"{entry.Key}={entry.Value}"));
        urlBuilder.Append(query);

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<StreamResponse>("GET", url, "application/octet-stream", default, cancellationToken);
    }

    /// <summary>
    /// Gets the user info.
    /// </summary>
    public Task<UserInfo> GetUserInfoAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Users/info");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<UserInfo>("GET", url, "application/json", default, cancellationToken);
    }

    /// <summary>
    /// Refreshes the JWT token.
    /// </summary>
    public Task<RefreshTokenResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Users/refresh-token");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<RefreshTokenResponse>("POST", url, "application/json", request, cancellationToken);
    }

    /// <summary>
    /// Revokes a refresh token.
    /// </summary>
    public Task<RevokeTokenResponse> RevokeTokenAsync(RevokeTokenRequest request, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Users/revoke-token");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<RevokeTokenResponse>("POST", url, "application/json", request, cancellationToken);
    }

    /// <summary>
    /// Get a list of refresh tokens for the specified user.
    /// </summary>
    public Task<ICollection<RefreshToken>> GetRefreshTokensAsync(string userId, CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Users/{userId}/tokens");
        urlBuilder.Replace("{userId}", Uri.EscapeDataString(Convert.ToString(userId, CultureInfo.InvariantCulture)));

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ICollection<RefreshToken>>("GET", url, "application/json", default, cancellationToken);
    }

}

public interface IWritersClient
{
    Task<ICollection<ExtensionDescription>> GetWriterDescriptionsAsync(CancellationToken cancellationToken = default);
}

public class WritersClient : IWritersClient
{
    private NexusOpenApiClient _client;
    
    internal WritersClient(NexusOpenApiClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Gets the list of writers.
    /// </summary>
    public Task<ICollection<ExtensionDescription>> GetWriterDescriptionsAsync(CancellationToken cancellationToken = default)
    {
        var urlBuilder = new StringBuilder();
        urlBuilder.Append("/api/v1/Writers/descriptions");

        var url = urlBuilder.ToString();
        return _client.InvokeAsync<ICollection<ExtensionDescription>>("GET", url, "application/json", default, cancellationToken);
    }

}

public class StreamResponse : IDisposable
{
    HttpResponseMessage _response;

    public StreamResponse(HttpResponseMessage response, Stream stream)
    {
        _response = response;

        Stream = stream;
    }

    public Stream Stream { get; }

    public void Dispose()
    {
        Stream.Dispose();
        _response.Dispose();
    }
}

public class NexusApiException : Exception
{
    public NexusApiException(string message) : base(message)
    {
        //
    }
}

internal class DisposableConfiguration : IDisposable
{
    private NexusOpenApiClient _client;

    public DisposableConfiguration(NexusOpenApiClient client)
    {
        _client = client;
    }

    public void Dispose()
    {
        _client.ClearConfiguration();
    }
}

public record ResourceCatalog (string Id, IDictionary<string, string>? Properties, ICollection<Resource>? Resources);

public record Resource (string Id, IDictionary<string, string>? Properties, ICollection<Representation>? Representations);

public record Representation (NexusDataType DataType, TimeSpan SamplePeriod, string? Detail, bool IsPrimary);

public enum NexusDataType
{
    UINT8,
    UINT16,
    UINT32,
    UINT64,
    INT8,
    INT16,
    INT32,
    INT64,
    FLOAT32,
    FLOAT64
}

public record CatalogTimeRange (DateTime Begin, DateTime End);

public record CatalogAvailability (IDictionary<string, double> Data);

public record CatalogMetadata (string? Contact, bool IsHidden, ICollection<string>? GroupMemberships, ResourceCatalog? Overrides);

public record Job (Guid Id, string Type, string Owner, object? Parameters);

public record ExportParameters (DateTime Begin, DateTime End, TimeSpan FilePeriod, string Type, ICollection<string> ResourcePaths, IDictionary<string, string> Configuration);

public record JobStatus (DateTime Start, TaskStatus Status, double Progress, string? ExceptionMessage, object? Result);

public enum TaskStatus
{
    Created,
    WaitingForActivation,
    WaitingToRun,
    Running,
    WaitingForChildrenToComplete,
    RanToCompletion,
    Canceled,
    Faulted
}

public record PackageReference (string Provider, IDictionary<string, string> Configuration);

public record ExtensionDescription (string Type, string? Description);

public record DataSourceRegistration (string Type, Uri ResourceLocator, IDictionary<string, string> Configuration, bool Publish, bool Disable);

public record AuthenticationSchemeDescription (string Scheme, string DisplayName);

public record UserInfo (string Name);

public record RefreshTokenResponse (string? JwtToken, string? RefreshToken, string? Error);

public record RefreshTokenRequest (string RefreshToken);

public record RevokeTokenResponse (string? Error);

public record RevokeTokenRequest (string Token);

public record RefreshToken (string Token, DateTime Created, DateTime Expires, bool IsExpired);



