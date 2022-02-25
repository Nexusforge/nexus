from __future__ import annotations

import json
import os
import tempfile
import uuid
from dataclasses import dataclass
from datetime import datetime, timedelta
from enum import Enum
from typing import Any, Awaitable, Dict, List, Type
from urllib.parse import quote

from httpx import AsyncClient, AsyncByteStream, Request, Response, codes

# 0 = Namespace
# 1 = ClientName
# 2 = NexusConfigurationHeaderKey
# 3 = AuthorizationHeaderKey
# 4 = SubClientFields
# 5 = SubClientFieldAssignment
# 6 = SubClientProperties
# 7 = SubClientSource
# 8 = ExceptionType
# 9 = Models
# 10 = SubClientInterfaceProperties

class StreamResponse:
    """A stream response."""

    _response: Response
    _stream: AsyncByteStream

    def __init__(self, response: Response, stream: AsyncByteStream):
        self._response = response
        self._stream = stream

    @property
    def stream(self) -> bool:
        """The stream."""
        return self._stream

    def __aexit__(self, exc_type, exc_value, exc_traceback): 
        self._stream.aclose()

class {8}(Exception):
    """A {8}."""

    def __init__(self, status_code: str, message: str):
        self.status_code = status_code
        self.message = message

    status_code: str
    """The exception status code."""

    message: str
    """The exception message."""

{9}
{7}

class {1}:
    """A client for the Nexus system."""
    
    _nexus_configuration_header_key: str = "{2}"
    _authorization_header_key: str = "{3}"

    _token_folder_path: str = os.path.join(tempfile.gettempdir(), "nexus", "tokens")

    _token_pair: TokenPair
    _http_client: AsyncClient
    _token_file_path: str

{4}

    # /// <param name="baseUrl">The base URL to connect to.</param>
    # public {1}(Uri baseUrl) : this(new HttpClient() { BaseAddress = baseUrl })
    # {
    #     //
    # }

    def __init__(self, http_client: AsyncClient):
        """
        Initializes a new instance of the {1}
        
            Args:

            http_client: The HTTP client to use.
        """

        if http_client.base_url is None:
            raise Exception("The base url of the HTTP client must be set.")

        self._http_client = http_client
        self._token_pair = None

{5}

    @property
    def is_authenticated(self) -> bool:
        """Gets a value which indicates if the user is authenticated."""
        return self._token_pair is not None

{6}

    def sign_in(self, token_pair: TokenPair) -> None:
        """Signs in the user.

        Args:
            token_pair: A pair of access and refresh tokens.
        """
        # _tokenFilePath = Path.Combine(_tokenFolderPath, Uri.EscapeDataString(tokenPair.RefreshToken) + ".json");
        
        # if (File.Exists(_tokenFilePath))
        # {
        #     tokenPair = JsonSerializer.Deserialize<TokenPair>(File.ReadAllText(_tokenFilePath), _options)
        #         ?? throw new Exception($"Unable to deserialize file {_tokenFilePath} into a token pair.");
        # }

        # else
        # {
        #     Directory.CreateDirectory(_tokenFolderPath);
        #     File.WriteAllText(_tokenFilePath, JsonSerializer.Serialize(tokenPair, _options));
        # }

        # _httpClient.DefaultRequestHeaders.Add(AuthorizationHeaderKey, $"Bearer {tokenPair.AccessToken}");

        # _tokenPair = tokenPair;

    # /// <inheritdoc />
    # public IDisposable AttachConfiguration(IDictionary<string, string> configuration)
    # {
    #     var encodedJson = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(configuration));

    #     _httpClient.DefaultRequestHeaders.Remove(NexusConfigurationHeaderKey);
    #     _httpClient.DefaultRequestHeaders.Add(NexusConfigurationHeaderKey, encodedJson);

    #     return new DisposableConfiguration(this);
    # }

    def clear_configuration(self) -> None:
        """Clears configuration data for all subsequent Nexus API requests."""
        # _httpClient.DefaultRequestHeaders.Remove(NexusConfigurationHeaderKey)
        pass

    async def invoke_async(self, type: Type, method: str, relative_url: str, accept_header_value: str, content: Any) -> Awaitable[Any]:

        # prepare request
        http_content: Any = None \
            if content is None \
            else json.dumps(content)

        request = self.build_request_message(method, relative_url, http_content, accept_header_value)

        # send request
        response = await self._http_client.send(request)

        # process response
        if not response.is_success:
            
            # try to refresh the access token
            if response.status_code == codes.UNAUTHORIZED and self._token_pair is not None:

                www_authenticate_header = response.headers.get("WWW-Authenticate")
                sign_out = True

                if www_authenticate_header is not None:

                    if www_authenticate_header.contains("The token expired at"):

                        new_request = self.build_request_message(method, relative_url, http_content, accept_header_value)

                        try:

                            new_response = await self.refresh_token_async(response, new_request)

                            if new_response is not None:
                                response.Dispose()
                                response = new_response
                                sign_out = False

                        except:
                            pass

                if sign_out:
                    self.sign_out()

            if not response.is_success:

                message = response.text
                status_code = f"N00.{response.status_code}"

                if not message:
                    raise NexusException(status_code, f"The HTTP request failed with status code {response.status_code}.")

                else:
                    raise NexusException(status_code, f"The HTTP request failed with status code {response.status_code}. The response message is: {message}")

        try:

            stream = await response.stream

            if type is object:
                return None

            elif type is StreamResponse:
                return StreamResponse(response, stream)

            else:

                returnValue = await JsonSerializer.deserialize_async(type, stream)

                if returnValue is None:
                    raise NexusException(f"N01", "Response data could not be deserialized.")

                return returnValue

        finally:
            if type is StreamResponse:
                response.close()
    
    def build_request_message(self, method: str, relative_url: str, http_content: Any, accept_header_value: str) -> Request:
       
        request_message = self._http_client.build_request(method, relative_url, content = http_content)
        request_message.headers["Content-Type"] = "application/json"

        if accept_header_value is not None:
            request_message.headers["Accept"] = accept_header_value

        return request_message

    # private async Task<HttpResponseMessage?> RefreshTokenAsync(
    #     HttpResponseMessage response, 
    #     HttpRequestMessage newRequest,
    #     CancellationToken cancellationToken)
    # {
    #     // see https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/dev/src/Microsoft.IdentityModel.Tokens/Validators.cs#L390

    #     if (_tokenPair is null || response.RequestMessage is null)
    #         throw new Exception("Refresh token or request message is null. This should never happen.");

    #     var refreshRequest = new RefreshTokenRequest(RefreshToken: _tokenPair.RefreshToken);
    #     var tokenPair = await Users.RefreshTokenAsync(refreshRequest);

    #     if (_tokenFilePath is not null)
    #     {
    #         Directory.CreateDirectory(_tokenFolderPath);
    #         File.WriteAllText(_tokenFilePath, JsonSerializer.Serialize(tokenPair, _options));
    #     }

    #     var authorizationHeaderValue = $"Bearer {tokenPair.AccessToken}";
    #     _httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
    #     _httpClient.DefaultRequestHeaders.Add(AuthorizationHeaderKey, authorizationHeaderValue);

    #     _tokenPair = tokenPair;

    #     return await _httpClient.SendAsync(newRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    # }

    def sign_out(self) -> None:
        # self._httpClient.DefaultRequestHeaders.Remove(AuthorizationHeaderKey);
        self._token_pair = None

