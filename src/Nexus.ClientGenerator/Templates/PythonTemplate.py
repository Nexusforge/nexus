# Python <= 3.9
from __future__ import annotations

import base64
import json
import os
import typing
from array import array
from dataclasses import dataclass
from datetime import datetime, timedelta
from enum import Enum
from pathlib import Path
from typing import (Any, AsyncIterable, Awaitable, Iterable, Optional, Type,
                    TypeVar, Union)
from urllib.parse import quote
from uuid import UUID

from httpx import AsyncClient, Request, Response, codes

from ._encoder import (JsonEncoder, JsonEncoderOptions, to_camel_case,
                      to_snake_case)

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

T = TypeVar("T")

def _to_string(value: Any) -> str:

    if type(value) is datetime:
        return value.isoformat()

    else:
        return str(value)

_json_encoder_options: JsonEncoderOptions = JsonEncoderOptions(
    property_name_encoder=to_camel_case,
    property_name_decoder=to_snake_case
)

class StreamResponse:
    """A stream response."""

    _response: Response

    def __init__(self, response: Response):
        self._response = response

    async def read_as_double(self) -> array[float]:
        """Reads the data as an array of floats."""
        
        byteBuffer = await self._response.aread()

        if len(byteBuffer) % 8 != 0:
            raise Exception("The data length is invalid.")

        doubleBuffer = array("d", byteBuffer)

        return doubleBuffer 

    @property
    def response(self) -> Response:
        """Gets the underlying response."""
        return self._response

    async def __aenter__(self) -> StreamResponse:
        return self

    async def __aexit__(self, exc_type, exc_value, exc_traceback): 
        await self._response.aclose()

class {{8}}(Exception):
    """A {{8}}."""

    def __init__(self, status_code: str, message: str):
        self.status_code = status_code
        self.message = message

    status_code: str
    """The exception status code."""

    message: str
    """The exception message."""

class _DisposableConfiguration:
    _client : {{1}}

    def __init__(self, client: {{1}}):
        self._client = client

    # "disposable" methods
    def __enter__(self):
        pass

    def __exit__(self, exc_type, exc_value, exc_traceback):
        self._client.clear_configuration()

{{9}}
{{7}}

class {{1}}:
    """A client for the Nexus system."""
    
    _nexus_configuration_header_key: str = "{{2}}"
    _authorization_header_key: str = "{{3}}"

    _token_folder_path: str = os.path.join(str(Path.home()), ".nexus-api", "tokens")

    _token_pair: Optional[TokenPair]
    _http_client: AsyncClient
    _token_file_path: Optional[str]

{{4}}

    @classmethod
    def create(cls, base_url: str) -> {{1}}:
        """
        Initializes a new instance of the {{1}}
        
            Args:
                base_url: The base URL to use.
        """
        return {{1}}(AsyncClient(base_url=base_url))

    def __init__(self, http_client: AsyncClient):
        """
        Initializes a new instance of the {{1}}
        
            Args:
                http_client: The HTTP client to use.
        """

        if http_client.base_url is None:
            raise Exception("The base url of the HTTP client must be set.")

        self._http_client = http_client
        self._token_pair = None

{{5}}

    @property
    def is_authenticated(self) -> bool:
        """Gets a value which indicates if the user is authenticated."""
        return self._token_pair is not None

{{6}}

    async def sign_in(self, refresh_token: str):
        """Signs in the user.

        Args:
            token_pair: The refresh token.
        """

        actual_refresh_token: str

        self._token_file_path = os.path.join(self._token_folder_path, quote(refresh_token, safe="") + ".json")
        
        if Path(self._token_file_path).is_file():
            with open(self._token_file_path) as json_file:
                jsonObject = json.load(json_file)
                actual_refresh_token = JsonEncoder.decode(str, jsonObject, _json_encoder_options)

        else:
            Path(self._token_folder_path).mkdir(parents=True, exist_ok=True)

            with open(self._token_file_path, "w") as json_file:
                encoded = JsonEncoder.encode(refresh_token, _json_encoder_options)
                json.dump(encoded, json_file, indent=4)
                actual_refresh_token = refresh_token
                
        await self._refresh_token(actual_refresh_token)

    def attach_configuration(self, configuration: Any) -> Any:
        """Attaches configuration data to subsequent Nexus API requests."""

        encoded_json = base64.b64encode(json.dumps(configuration).encode("utf-8")).decode("utf-8")

        if self._nexus_configuration_header_key in self._http_client.headers:
            del self._http_client.headers[self._nexus_configuration_header_key]

        self._http_client.headers[self._nexus_configuration_header_key] = encoded_json

        return _DisposableConfiguration(self)

    def clear_configuration(self) -> None:
        """Clears configuration data for all subsequent Nexus API requests."""

        if self._nexus_configuration_header_key in self._http_client.headers:
            del self._http_client.headers[self._nexus_configuration_header_key]

    async def _invoke(self, typeOfT: Type[T], method: str, relative_url: str, accept_header_value: Optional[str], content_type_value: Optional[str], content: Union[None, str, bytes, Iterable[bytes], AsyncIterable[bytes]]) -> T:

        # prepare request
        request = self._build_request_message(method, relative_url, content, content_type_value, accept_header_value)

        # send request
        response = await self._http_client.send(request)

        # process response
        if not response.is_success:
            
            # try to refresh the access token
            if response.status_code == codes.UNAUTHORIZED and self._token_pair is not None:

                www_authenticate_header = response.headers.get("WWW-Authenticate")
                sign_out = True

                if www_authenticate_header is not None:

                    if "The token expired at" in www_authenticate_header:

                        try:
                            await self._refresh_token(self._token_pair.refresh_token)

                            new_request = self._build_request_message(method, relative_url, content, content_type_value, accept_header_value)
                            new_response = await self._http_client.send(new_request)

                            if new_response is not None:
                                await response.aclose()
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

            if typeOfT is type(None):
                return typing.cast(T, type(None))

            elif typeOfT is StreamResponse:
                return typing.cast(T, StreamResponse(response))

            else:

                jsonObject = json.loads(response.text)
                return_value = JsonEncoder.decode(typeOfT, jsonObject, _json_encoder_options)

                if return_value is None:
                    raise NexusException(f"N01", "Response data could not be deserialized.")

                return return_value

        finally:
            if typeOfT is not StreamResponse:
                await response.aclose()
    
    def _build_request_message(self, method: str, relative_url: str, content: Any, content_type_value: Optional[str], accept_header_value: Optional[str]) -> Request:
       
        request_message = self._http_client.build_request(method, relative_url, content = content)

        if content_type_value is not None:
            request_message.headers["Content-Type"] = content_type_value

        if accept_header_value is not None:
            request_message.headers["Accept"] = accept_header_value

        return request_message

    async def _refresh_token(self, refresh_token: str):
        # see https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/dev/src/Microsoft.IdentityModel.Tokens/Validators.cs#L390

        refresh_request = RefreshTokenRequest(refresh_token)
        token_pair = await self.users.refresh_token(refresh_request)

        if self._token_file_path is not None:
            Path(self._token_folder_path).mkdir(parents=True, exist_ok=True)
            
            with open(self._token_file_path, "w") as json_file:
                encoded = JsonEncoder.encode(token_pair.refresh_token, _json_encoder_options)
                json.dump(encoded, json_file, indent=4)

        authorizationHeaderValue = f"Bearer {token_pair.access_token}"

        if self._authorization_header_key in self._http_client.headers:
            del self._http_client.headers[self._authorization_header_key]

        self._http_client.headers[self._authorization_header_key] = authorizationHeaderValue
        self._token_pair = token_pair

    def sign_out(self) -> None:

        if self._authorization_header_key in self._http_client.headers:
            del self._http_client.headers[self._authorization_header_key]

        self._token_pair = None

    # "disposable" methods
    async def __aenter__(self) -> {{1}}:
        return self

    async def __aexit__(self, exc_type, exc_value, exc_traceback):
        if (self._http_client is not None):
            await self._http_client.aclose()
