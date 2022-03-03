# Python <= 3.9
from __future__ import annotations

import base64
import dataclasses
import json
import os
import re
import tempfile
import typing
from dataclasses import dataclass
from datetime import datetime, timedelta
from enum import Enum
from json import JSONEncoder
from pathlib import Path
from types import GenericAlias
from typing import Any, Awaitable, Optional, Tuple, Type, TypeVar
from urllib.parse import quote
from uuid import UUID

from httpx import AsyncByteStream, AsyncClient, Request, Response, codes

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
snake_case_pattern = re.compile('((?<=[a-z0-9])[A-Z]|(?!^)[A-Z](?=[a-z]))')
timespan_pattern = re.compile('^(?:([0-9]+)\\.)?([0-9]{2}):([0-9]{2}):([0-9]{2})(?:\\.([0-9]+))?$')

class _MyEncoder(JSONEncoder):

    def default(self, o: Any):
        return self._convert(o)

    def _convert(self, value: Any) -> Any:

        result: Any

        # date/time
        if isinstance(value, datetime):
            result = value.isoformat()

        # timedelta
        elif isinstance(value, timedelta):
            hours, remainder = divmod(value.seconds, 3600)
            minutes, seconds = divmod(remainder, 60)
            result = f"{int(value.days)}.{int(hours):02}:{int(minutes):02}:{int(seconds):02}.{value.microseconds}"

        # enum
        elif isinstance(value, Enum):
            result = value.value

        # dataclass
        elif dataclasses.is_dataclass(value):
            result = {}

            for (key, local_value) in value.__dict__.items():
                result[self._to_camel_case(key)] = self._convert(local_value)

        # else
        else:
            result = value

        return result

    def _to_camel_case(self, value: str) -> str:
        components = value.split("_")
        return components[0] + ''.join(x.title() for x in components[1:])

def _decode(cls: Type[T], data: Any) -> T:

    if (data is None):
        return typing.cast(T, None)

    if isinstance(cls, GenericAlias):

        origin = typing.cast(Type, typing.get_origin(cls))
        args = typing.get_args(cls)

        # list
        if (issubclass(origin, list)):

            listType = args[0]
            instance1: list = list()

            for value in data:
                instance1.append(_decode(listType, value))

            return typing.cast(T, instance1)
        
        # dict
        elif (issubclass(origin, dict)):

            keyType = args[0]
            valueType = args[1]

            instance2: dict = dict()

            for key, value in data.items():
                key = snake_case_pattern.sub(r'_\1', key).lower()
                instance2[_decode(keyType, key)] = _decode(valueType, value)

            return typing.cast(T, instance2)

        else:
            raise Exception(f"Type {str(origin)} cannot be deserialized.")

    elif issubclass(cls, datetime):
        return typing.cast(T, datetime.strptime(data[:-1], "%Y-%m-%dT%H:%M:%S.%f"))

    elif issubclass(cls, timedelta):
        # ^(?:([0-9]+)\.)?([0-9]{2}):([0-9]{2}):([0-9]{2})(?:\.([0-9]+))?$
        # 12:08:07
        # 12:08:07.1250000
        # 3000.00:08:07
        # 3000.00:08:07.1250000
        match = timespan_pattern.match(data)

        if match:
            days = int(match.group(1)) if match.group(1) else 0
            hours = int(match.group(2)) if match.group(2) else 0
            minutes = int(match.group(3)) if match.group(3) else 0
            seconds = int(match.group(4)) if match.group(4) else 0
            milliseconds = int(match.group(5)) if match.group(5) else 0

            return typing.cast(T, timedelta(days=days, hours=hours, minutes=minutes, seconds=seconds, milliseconds=milliseconds))

        else:
            raise Exception(f"Unable to deserialize {data} into value of type timedelta.")

    elif issubclass(cls, UUID):
        return typing.cast(T, UUID(data))
       
    elif dataclasses.is_dataclass(cls):

        p = []

        for name, value in data.items():

            type_hints = typing.get_type_hints(cls)
            name = snake_case_pattern.sub(r'_\1', name).lower()
            parameterType = typing.cast(Type, type_hints.get(name))
            value = _decode(parameterType, value)

            p.append(value)

        parameters_count = len(p)

        if (parameters_count == 0): return cls()
        if (parameters_count == 1): return cls(p[0])
        if (parameters_count == 2): return cls(p[0], p[1])
        if (parameters_count == 3): return cls(p[0], p[1], p[2])
        if (parameters_count == 4): return cls(p[0], p[1], p[2], p[3])
        if (parameters_count == 5): return cls(p[0], p[1], p[2], p[3], p[4])
        if (parameters_count == 6): return cls(p[0], p[1], p[2], p[3], p[4], p[5])
        if (parameters_count == 7): return cls(p[0], p[1], p[2], p[3], p[4], p[5], p[6])
        if (parameters_count == 8): return cls(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7])
        if (parameters_count == 9): return cls(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7], p[8])
        if (parameters_count == 10): return cls(p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7], p[8], p[9])

        raise Exception("Dataclasses with more than 10 parameters cannot be deserialized.")

    else:
        return data

def _to_string(value: Any) -> str:

    if type(value) is datetime:
        return value.isoformat()

    else:
        return str(value)

class StreamResponse:
    """A stream response."""

    _response: Response
    _stream: AsyncByteStream

    def __init__(self, response: Response, stream: AsyncByteStream):
        self._response = response
        self._stream = stream

    @property
    def stream(self) -> AsyncByteStream:
        """The stream."""
        return self._stream

    def __aexit__(self, exc_type, exc_value, exc_traceback) -> Awaitable[None]: 
        return self._stream.aclose()

class {8}(Exception):
    """A {8}."""

    def __init__(self, status_code: str, message: str):
        self.status_code = status_code
        self.message = message

    status_code: str
    """The exception status code."""

    message: str
    """The exception message."""

class _DisposableConfiguration:
    _client : {1}

    def __init__(self, client: {1}):
        self._client = client

    # "disposable" methods
    def __enter__(self):
        pass

    def __exit__(self, exc_type, exc_value, exc_traceback):
        self._client.clear_configuration()

{9}
{7}

class {1}:
    """A client for the Nexus system."""
    
    _nexus_configuration_header_key: str = "{2}"
    _authorization_header_key: str = "{3}"

    _token_folder_path: str = os.path.join(tempfile.gettempdir(), "nexus", "tokens")

    _token_pair: Optional[TokenPair]
    _http_client: AsyncClient
    _token_file_path: Optional[str]

{4}

    @classmethod
    def create(cls, base_url: str) -> {1}:
        """
        Initializes a new instance of the {1}
        
            Args:
                base_url: The base URL to use.
        """
        return {1}(AsyncClient(base_url=base_url))

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

        self._token_file_path = os.path.join(self._token_folder_path, quote(token_pair.refresh_token, safe="") + ".json")
        
        if Path(self._token_file_path).is_file():
            with open(self._token_file_path) as json_file:
                jsonObject = json.load(json_file)
                token_pair = _decode(TokenPair, jsonObject)

        else:
            Path(self._token_folder_path).mkdir(parents=True, exist_ok=True)

            with open(self._token_file_path, "w") as json_file:
                json.dump(token_pair, json_file, indent=4, cls=_MyEncoder)
                
        self._http_client.headers[self._authorization_header_key] = f"Bearer {token_pair.access_token}"
        self._token_pair = token_pair

    def attach_configuration(self, configuration: dict[str, str]) -> Any:
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

    async def _invoke_async(self, typeOfT: Type[T], method: str, relative_url: str, accept_header_value: str, content: Any) -> T:

        # prepare request
        http_content: Any = None \
            if content is None \
            else json.dumps(content, cls=_MyEncoder)

        request = self._build_request_message(method, relative_url, http_content, accept_header_value)

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
                            await self._refresh_token_async()

                            new_request = self._build_request_message(method, relative_url, http_content, accept_header_value)
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
                return typing.cast(T, StreamResponse(response, typing.cast(AsyncByteStream, response.stream)))

            else:

                jsonObject = json.loads(response.text)
                return_value = _decode(typeOfT, jsonObject)

                if return_value is None:
                    raise NexusException(f"N01", "Response data could not be deserialized.")

                return return_value

        finally:
            if typeOfT is StreamResponse:
                await response.aclose()
    
    def _build_request_message(self, method: str, relative_url: str, http_content: Any, accept_header_value: str) -> Request:
       
        request_message = self._http_client.build_request(method, relative_url, content = http_content)
        request_message.headers["Content-Type"] = "application/json"

        if accept_header_value is not None:
            request_message.headers["Accept"] = accept_header_value

        return request_message

    async def _refresh_token_async(self):
        # see https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/dev/src/Microsoft.IdentityModel.Tokens/Validators.cs#L390

        if self._token_pair is None:
            raise Exception("Refresh token is null. This should never happen.")

        refresh_request = RefreshTokenRequest(refresh_token=self._token_pair.refresh_token)
        token_pair = await self.users.refresh_token(refresh_request)

        if self._token_file_path is not None:
            Path(self._token_folder_path).mkdir(parents=True, exist_ok=True)
            
            with open(self._token_file_path, "w") as json_file:
                json.dump(token_pair, json_file, indent=4, cls=_MyEncoder)

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
    async def __aenter__(self) -> {1}:
        return self

    async def __aexit__(self, exc_type, exc_value, exc_traceback):
        if (self._http_client is not None):
            await self._http_client.aclose()

    # "extension" methods
    def attach_configuration2(self, *configuration: Tuple[str, str]) -> Any:
        """Attaches configuration data to subsequent Nexus API requests."""

        dict_configuration = { key: value for key, value in configuration }
        return self.attach_configuration(dict_configuration)
        
