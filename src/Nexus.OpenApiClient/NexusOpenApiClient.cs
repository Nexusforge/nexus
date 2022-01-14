using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Client
{
    /// <summary>
    /// The OpenAPI client for the Nexus system.
    /// </summary>
    public class NexusOpenApiClient
    {
        private const string NexusConfigurationHeaderKey = "Nexus-Configuration";

        private bool _isRefreshRequest;

        private string? _jwtToken;
        private string? _refreshToken;

        private HttpClient _httpClient;

        private CatalogsClient _catalogs;
        private DataClient _data;
        private JobsClient _jobs;
        private UsersClient _users;

        /// <summary>
        /// Initializes a new instances of the <see cref="NexusOpenApiClient"/>.
        /// </summary>
        /// <param name="baseUrl">The base URL to connect to.</param>
        /// <param name="httpClient">An optional HTTP client.</param>
        public NexusOpenApiClient(string baseUrl, HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();

            _catalogs = new CatalogsClient(baseUrl, _httpClient) { Client = this };
            _data = new DataClient(baseUrl, _httpClient) { Client = this };
            _jobs = new JobsClient(baseUrl, _httpClient) { Client = this };
            _users = new UsersClient(baseUrl, _httpClient) { Client = this };
        }

        /// <summary>
        /// Gets a value which indicates if the user is authenticated.
        /// </summary>
        public bool IsAuthenticated => _jwtToken != null;

        /// <summary>
        /// Gets the catalogs client.
        /// </summary>
        public ICatalogsClient Catalogs => _catalogs;

        /// <summary>
        /// Gets the data client.
        /// </summary>
        public IDataClient Data => _data;

        /// <summary>
        /// Gets the jobs client.
        /// </summary>
        public IJobsClient Jobs => _jobs;

        /// <summary>
        /// Gets the users client.
        /// </summary>
        public IUsersClient Users => _users;

        /// <summary>
        /// Attempts to sign in the user.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <param name="password">The user password.</param>
        /// <returns>A task.</returns>
        /// <exception cref="SecurityException">Thrown when the authentication fails.</exception>
        public async Task PasswordSignInAsync(string userId, string password)
        {
            var authenticateRequest = new AuthenticateRequest() { UserId = userId, Password = password };
            var authenticateResponse = await this.Users.AuthenticateAsync(authenticateRequest);

            if (authenticateResponse.Error is not null)
                throw new SecurityException($"Unable to authenticate. Reason: {authenticateResponse.Error}");

            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {authenticateResponse.JwtToken}");

            _jwtToken = authenticateResponse.JwtToken;
            _refreshToken = authenticateResponse.RefreshToken;
        }

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

        internal async Task ProcessResponseAsync(HttpClient client, HttpResponseMessage response, CancellationToken cancellationToken)
        {
            // Workaround for https://github.com/RicoSuter/NSwag/issues/1559

            // do not process the refresh request response
            if (_isRefreshRequest)
                return;

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var reason = await response.Content.ReadAsStringAsync();

                // possible responses:
                // ___________________
                // not logged in:                       The current user is not authorized to access the catalog '/IN_MEMORY/TEST/RESTRICTED'.
                // expired token:                       Lifetime validation failed.
                // valid token but wrong signature:     Signature validation failed.
                // other reasons token:                 The bearer token could not be validated.

                // new password login required, maybe the server has restarted
                if (reason == "Signature validation failed.")
                {
                    this.SignOut();
                    return;
                }

                // token has expired, try to refresh
                if (reason == "Lifetime validation failed.")
                {
                    try
                    {
                        _isRefreshRequest = true;

                        if (_refreshToken is null || response.RequestMessage is null)
                            throw new Exception("Refresh token or request message is null. This should never happen.");

                        _httpClient.DefaultRequestHeaders.Remove("Authorization");

                        var refreshRequest = new RefreshTokenRequest() { RefreshToken = _refreshToken };
                        var refreshResponse = await this.Users.RefreshTokenAsync(refreshRequest);

                        if (refreshResponse.Error is not null)
                        {
                            this.SignOut();
                            return;
                        }

                        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {refreshResponse.JwtToken}");

                        _jwtToken = refreshResponse.JwtToken;
                        _refreshToken = refreshResponse.RefreshToken;

                        var clonedMessage = await CloneHttpMessageAsync(response.RequestMessage);
                        var newResponse = await client.SendAsync(clonedMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                        response.Content = newResponse.Content;
                        response.RequestMessage = newResponse.RequestMessage;
                        response.StatusCode = newResponse.StatusCode;
                        response.ReasonPhrase = newResponse.ReasonPhrase;
                        response.Version = newResponse.Version;
                    }
                    finally
                    {
                        _isRefreshRequest = false;
                    }
                }
            }
        }

        private void SignOut()
        {
            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            _refreshToken = null;
            _jwtToken = null;
        }

        // from https://stackoverflow.com/a/46026230
        private static async Task<HttpRequestMessage> CloneHttpMessageAsync(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Content = await CloneHttpMessageContentAsync(request.Content),
                Version = request.Version
            };

            foreach (var property in request.Options)
            {
                var value = property.Value as string;

                if (value is not null)
                    clone.Options.Set(new HttpRequestOptionsKey<string>(property.Key), value);
            }

            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }

        private static async Task<HttpContent?> CloneHttpMessageContentAsync(HttpContent? content)
        {
            if (content == null) 
                return null;

            var stream = new MemoryStream();

            await content.CopyToAsync(stream);
            stream.Position = 0;

            var clone = new StreamContent(stream);

            foreach (var header in content.Headers)
            {
                clone.Headers.Add(header.Key, header.Value);
            }

            return clone;
        }
    }
}