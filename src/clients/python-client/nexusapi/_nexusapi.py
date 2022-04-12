# Python <= 3.9
from __future__ import annotations

import base64
import dataclasses
import json
import os
import re
import typing
from array import array
from dataclasses import dataclass
from datetime import datetime, timedelta
from enum import Enum
from json import JSONEncoder
from pathlib import Path
from typing import Any, Awaitable, Optional, Tuple, Type, TypeVar, Union
from urllib.parse import quote
from uuid import UUID

from httpx import AsyncClient, Request, Response, codes

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

    if data is None:
        return typing.cast(T, None)

    origin = typing.cast(Type, typing.get_origin(cls))
    args = typing.get_args(cls)

    if origin is not None:

        # Optional
        if origin is Union and type(None) in args:

            baseType = args[0]
            instance3 = _decode(baseType, data)

            return typing.cast(T, instance3)

        # list
        elif issubclass(origin, list):

            listType = args[0]
            instance1: list = list()

            for value in data:
                instance1.append(_decode(listType, value))

            return typing.cast(T, instance1)
        
        # dict
        elif issubclass(origin, dict):

            keyType = args[0]
            valueType = args[1]

            instance2: dict = dict()

            for key, value in data.items():
                key = snake_case_pattern.sub(r'_\1', key).lower()
                instance2[_decode(keyType, key)] = _decode(valueType, value)

            return typing.cast(T, instance2)

        # default
        else:
            raise Exception(f"Type {str(origin)} cannot be deserialized.")

    # datetime
    elif issubclass(cls, datetime):
        return typing.cast(T, datetime.strptime(data[:-1], "%Y-%m-%dT%H:%M:%S.%f"))

    # timedelta
    elif issubclass(cls, timedelta):
        # ^(?:([0-9]+)\.)?([0-9]Nexus-Configuration):([0-9]Nexus-Configuration):([0-9]Nexus-Configuration)(?:\.([0-9]+))?$
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

    # UUID
    elif issubclass(cls, UUID):
        return typing.cast(T, UUID(data))
       
    # dataclass
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

    # default
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

    def __init__(self, response: Response):
        self._response = response

    async def get_data(self) -> array[float]:
        """Reads the data as an array of floats."""
        
        byteBuffer = await self._response.aread()
        doubleBuffer = array('d', byteBuffer)

        return doubleBuffer 

    async def __aenter__(self) -> StreamResponse:
        return self

    async def __aexit__(self, exc_type, exc_value, exc_traceback): 
        await self._response.aclose()

class NexusException(Exception):
    """A NexusException."""

    def __init__(self, status_code: str, message: str):
        self.status_code = status_code
        self.message = message

    status_code: str
    """The exception status code."""

    message: str
    """The exception message."""

class _DisposableConfiguration:
    _client : NexusAsyncClient

    def __init__(self, client: NexusAsyncClient):
        self._client = client

    # "disposable" methods
    def __enter__(self):
        pass

    def __exit__(self, exc_type, exc_value, exc_traceback):
        self._client.clear_configuration()

@dataclass
class ResourceCatalog:
    """
    A catalog is a top level element and holds a list of resources.

    Args:
        id: The identifier.
        properties: The map of properties.
        resources: The list of representations.
    """

    id: str
    """The identifier."""

    properties: Optional[dict[str, str]]
    """The map of properties."""

    resources: Optional[list[Resource]]
    """The list of representations."""


@dataclass
class Resource:
    """
    A resource is part of a resource catalog and holds a list of representations.

    Args:
        id: The identifier.
        properties: The map of properties.
        representations: The list of representations.
    """

    id: str
    """The identifier."""

    properties: Optional[dict[str, str]]
    """The map of properties."""

    representations: Optional[list[Representation]]
    """The list of representations."""


@dataclass
class Representation:
    """
    A representation is part of a resource.

    Args:
        data_type: The data type.
        sample_period: The sample period.
        kind: The representation kind.
    """

    data_type: NexusDataType
    """The data type."""

    sample_period: timedelta
    """The sample period."""

    kind: RepresentationKind
    """The representation kind."""


class NexusDataType(Enum):
    """Specifies the Nexus data type."""

    UINT8 = "UINT8"
    """UINT8""",

    UINT16 = "UINT16"
    """UINT16""",

    UINT32 = "UINT32"
    """UINT32""",

    UINT64 = "UINT64"
    """UINT64""",

    INT8 = "INT8"
    """INT8""",

    INT16 = "INT16"
    """INT16""",

    INT32 = "INT32"
    """INT32""",

    INT64 = "INT64"
    """INT64""",

    FLOAT32 = "FLOAT32"
    """FLOAT32""",

    FLOAT64 = "FLOAT64"
    """FLOAT64"""


class RepresentationKind(Enum):
    """Specifies the representation kind."""

    ORIGINAL = "ORIGINAL"
    """Original""",

    RESAMPLED = "RESAMPLED"
    """Resampled""",

    MEAN = "MEAN"
    """Mean""",

    MEAN_POLAR_DEG = "MEAN_POLAR_DEG"
    """MeanPolarDeg""",

    MIN = "MIN"
    """Min""",

    MAX = "MAX"
    """Max""",

    STD = "STD"
    """Std""",

    RMS = "RMS"
    """Rms""",

    MIN_BITWISE = "MIN_BITWISE"
    """MinBitwise""",

    MAX_BITWISE = "MAX_BITWISE"
    """MaxBitwise""",

    SUM = "SUM"
    """Sum"""


@dataclass
class CatalogTimeRange:
    """
    A catalog time range.

    Args:
        begin: The date/time of the first data in the catalog.
        end: The date/time of the last data in the catalog.
    """

    begin: datetime
    """The date/time of the first data in the catalog."""

    end: datetime
    """The date/time of the last data in the catalog."""


@dataclass
class CatalogAvailability:
    """
    The catalog availability.

    Args:
        data: The actual availability data.
    """

    data: dict[str, float]
    """The actual availability data."""


@dataclass
class CatalogMetadata:
    """
    A structure for catalog metadata.

    Args:
        contact: The contact.
        is_hidden: A boolean which indicates if the catalog should be hidden.
        group_memberships: A list of groups the catalog is part of.
        overrides: Overrides for the catalog.
    """

    contact: Optional[str]
    """The contact."""

    is_hidden: bool
    """A boolean which indicates if the catalog should be hidden."""

    group_memberships: Optional[list[str]]
    """A list of groups the catalog is part of."""

    overrides: Optional[ResourceCatalog]
    """Overrides for the catalog."""


@dataclass
class Job:
    """
    Description of a job.

    Args:
        id: 06f8eb30-5924-4a71-bdff-322f92343f5b
        type: export
        owner: test@nexus.localhost
        parameters: Job parameters.
    """

    id: UUID
    """06f8eb30-5924-4a71-bdff-322f92343f5b"""

    type: str
    """export"""

    owner: str
    """test@nexus.localhost"""

    parameters: Optional[object]
    """Job parameters."""


@dataclass
class ExportParameters:
    """
    A structure for export parameters.

    Args:
        begin: 2020-02-01T00:00:00Z
        end: 2020-02-02T00:00:00Z
        file_period: 00:00:00
        type: Nexus.Writers.Csv
        resource_paths: ["/IN_MEMORY/TEST/ACCESSIBLE/T1/1_s_mean", "/IN_MEMORY/TEST/ACCESSIBLE/V1/1_s_mean"]
        configuration: { "RowIndexFormat": "Index", "SignificantFigures": "4" }
    """

    begin: datetime
    """2020-02-01T00:00:00Z"""

    end: datetime
    """2020-02-02T00:00:00Z"""

    file_period: timedelta
    """00:00:00"""

    type: str
    """Nexus.Writers.Csv"""

    resource_paths: list[str]
    """["/IN_MEMORY/TEST/ACCESSIBLE/T1/1_s_mean", "/IN_MEMORY/TEST/ACCESSIBLE/V1/1_s_mean"]"""

    configuration: dict[str, str]
    """{ "RowIndexFormat": "Index", "SignificantFigures": "4" }"""


@dataclass
class JobStatus:
    """
    Describes the status of the job.

    Args:
        start: The start date/time.
        status: The status.
        progress: The progress from 0 to 1.
        exception_message: An optional exception message.
        result: The optional result.
    """

    start: datetime
    """The start date/time."""

    status: TaskStatus
    """The status."""

    progress: float
    """The progress from 0 to 1."""

    exception_message: Optional[str]
    """An optional exception message."""

    result: Optional[object]
    """The optional result."""


class TaskStatus(Enum):
    """"""

    CREATED = "CREATED"
    """Created""",

    WAITING_FOR_ACTIVATION = "WAITING_FOR_ACTIVATION"
    """WaitingForActivation""",

    WAITING_TO_RUN = "WAITING_TO_RUN"
    """WaitingToRun""",

    RUNNING = "RUNNING"
    """Running""",

    WAITING_FOR_CHILDREN_TO_COMPLETE = "WAITING_FOR_CHILDREN_TO_COMPLETE"
    """WaitingForChildrenToComplete""",

    RAN_TO_COMPLETION = "RAN_TO_COMPLETION"
    """RanToCompletion""",

    CANCELED = "CANCELED"
    """Canceled""",

    FAULTED = "FAULTED"
    """Faulted"""


@dataclass
class PackageReference:
    """
    A package reference.

    Args:
        provider: The provider which loads the package.
        configuration: The configuration of the package reference.
    """

    provider: str
    """The provider which loads the package."""

    configuration: dict[str, str]
    """The configuration of the package reference."""


@dataclass
class ExtensionDescription:
    """
    An extension description.

    Args:
        type: The extension type.
        description: An optional description.
    """

    type: str
    """The extension type."""

    description: Optional[str]
    """An optional description."""


@dataclass
class DataSourceRegistration:
    """
    A backend source.

    Args:
        type: The type of the backend source.
        resource_locator: An URL which points to the data.
        configuration: Configuration parameters for the instantiated source.
        publish: A boolean which indicates if the found catalogs should be available for everyone.
        disable: A boolean which indicates if this backend source should be ignored.
    """

    type: str
    """The type of the backend source."""

    resource_locator: str
    """An URL which points to the data."""

    configuration: dict[str, str]
    """Configuration parameters for the instantiated source."""

    publish: bool
    """A boolean which indicates if the found catalogs should be available for everyone."""

    disable: bool
    """A boolean which indicates if this backend source should be ignored."""


@dataclass
class AuthenticationSchemeDescription:
    """
    Describes an OpenID connect provider.

    Args:
        scheme: The scheme.
        display_name: The display name.
    """

    scheme: str
    """The scheme."""

    display_name: str
    """The display name."""


@dataclass
class TokenPair:
    """
    A token pair.

    Args:
        access_token: The JWT token.
        refresh_token: The refresh token.
    """

    access_token: str
    """The JWT token."""

    refresh_token: str
    """The refresh token."""


@dataclass
class RefreshTokenRequest:
    """
    A refresh token request.

    Args:
        refresh_token: The refresh token.
    """

    refresh_token: str
    """The refresh token."""


@dataclass
class RevokeTokenRequest:
    """
    A revoke token request.

    Args:
        token: The refresh token.
    """

    token: str
    """The refresh token."""


@dataclass
class NexusUser:
    """
    Represents a user.

    Args:
        id: The user identifier.
        name: The user name.
        refresh_tokens: The list of refresh tokens.
        claims: The map of claims.
    """

    id: str
    """The user identifier."""

    name: str
    """The user name."""

    refresh_tokens: list[RefreshToken]
    """The list of refresh tokens."""

    claims: dict[str, NexusClaim]
    """The map of claims."""


@dataclass
class RefreshToken:
    """
    A refresh token.

    Args:
        token: The refresh token.
        created: The date/time when the token was created.
        expires: The date/time when the token expires.
        revoked: The date/time when the token was revoked.
        replaced_by_token: The token that replaced this one.
        is_expired: A boolean that indicates if the token has expired.
        is_revoked: A boolean that indicates if the token has been revoked.
        is_active: A boolean that indicates if the token is active.
    """

    token: str
    """The refresh token."""

    created: datetime
    """The date/time when the token was created."""

    expires: datetime
    """The date/time when the token expires."""

    revoked: Optional[datetime]
    """The date/time when the token was revoked."""

    replaced_by_token: Optional[str]
    """The token that replaced this one."""

    is_expired: bool
    """A boolean that indicates if the token has expired."""

    is_revoked: bool
    """A boolean that indicates if the token has been revoked."""

    is_active: bool
    """A boolean that indicates if the token is active."""


@dataclass
class NexusClaim:
    """
    Represents a claim.

    Args:
        type: The claim type.
        value: The claim value.
    """

    type: str
    """The claim type."""

    value: str
    """The claim value."""



class ArtifactsClient:
    """Provides methods to interact with artifacts."""

    _client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self._client = client

    def download(self, artifact_id: str) -> Awaitable[StreamResponse]:
        """
        Gets the specified artifact.

        Args:
            artifact_id: The artifact identifier.
        """

        url = "/api/v1/artifacts/{artifactId}"
        url = url.replace("{artifactId}", quote(str(artifact_id), safe=""))

        return self._client._invoke_async(StreamResponse, "GET", url, "application/octet-stream", None)


class CatalogsClient:
    """Provides methods to interact with catalogs."""

    _client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self._client = client

    def get(self, catalog_id: str) -> Awaitable[ResourceCatalog]:
        """
        Gets the specified catalog.

        Args:
            catalog_id: The catalog identifier.
        """

        url = "/api/v1/catalogs/{catalogId}"
        url = url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self._client._invoke_async(ResourceCatalog, "GET", url, "application/json", None)

    def get_child_catalog_ids(self, catalog_id: str) -> Awaitable[list[str]]:
        """
        Gets a list of child catalog identifiers for the provided parent catalog identifier.

        Args:
            catalog_id: The parent catalog identifier.
        """

        url = "/api/v1/catalogs/{catalogId}/child-catalog-ids"
        url = url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self._client._invoke_async(list[str], "GET", url, "application/json", None)

    def get_time_range(self, catalog_id: str) -> Awaitable[CatalogTimeRange]:
        """
        Gets the specified catalog's time range.

        Args:
            catalog_id: The catalog identifier.
        """

        url = "/api/v1/catalogs/{catalogId}/timerange"
        url = url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self._client._invoke_async(CatalogTimeRange, "GET", url, "application/json", None)

    def get_availability(self, catalog_id: str, begin: datetime, end: datetime) -> Awaitable[CatalogAvailability]:
        """
        Gets the specified catalog availability.

        Args:
            catalog_id: The catalog identifier.
            begin: Start date/time.
            end: End date/time.
        """

        url = "/api/v1/catalogs/{catalogId}/availability"
        url = url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        queryValues: dict[str, str] = {
            "begin": quote(_to_string(begin), safe=""),
            "end": quote(_to_string(end), safe=""),
        }

        query: str = "?" + "&".join(f"{key}={value}" for (key, value) in queryValues.items())
        url += query

        return self._client._invoke_async(CatalogAvailability, "GET", url, "application/json", None)

    def get_attachement_stream(self, catalog_id: str, attachment_id: str) -> Awaitable[StreamResponse]:
        """
        Gets the specified attachment.

        Args:
            catalog_id: The catalog identifier.
            attachment_id: The attachment identifier.
        """

        url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}/content"
        url = url.replace("{catalogId}", quote(str(catalog_id), safe=""))
        url = url.replace("{attachmentId}", quote(str(attachment_id), safe=""))

        return self._client._invoke_async(StreamResponse, "GET", url, "application/octet-stream", None)

    def get_metadata(self, catalog_id: str) -> Awaitable[CatalogMetadata]:
        """
        Gets the catalog metadata.

        Args:
            catalog_id: The catalog identifier.
        """

        url = "/api/v1/catalogs/{catalogId}/metadata"
        url = url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self._client._invoke_async(CatalogMetadata, "GET", url, "application/json", None)

    def put_metadata(self, catalog_id: str, catalog_metadata: CatalogMetadata) -> Awaitable[None]:
        """
        Puts the catalog metadata.

        Args:
            catalog_id: The catalog identifier.
        """

        url = "/api/v1/catalogs/{catalogId}/metadata"
        url = url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self._client._invoke_async(type(None), "PUT", url, "", catalog_metadata)


class DataClient:
    """Provides methods to interact with data."""

    _client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self._client = client

    def get_stream(self, resource_path: str, begin: datetime, end: datetime) -> Awaitable[StreamResponse]:
        """
        Gets the requested data.

        Args:
            resource_path: The path to the resource data to stream.
            begin: Start date/time.
            end: End date/time.
        """

        url = "/api/v1/data"

        queryValues: dict[str, str] = {
            "resourcePath": quote(_to_string(resource_path), safe=""),
            "begin": quote(_to_string(begin), safe=""),
            "end": quote(_to_string(end), safe=""),
        }

        query: str = "?" + "&".join(f"{key}={value}" for (key, value) in queryValues.items())
        url += query

        return self._client._invoke_async(StreamResponse, "GET", url, "application/octet-stream", None)


class JobsClient:
    """Provides methods to interact with jobs."""

    _client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self._client = client

    def export(self, parameters: ExportParameters) -> Awaitable[Job]:
        """
        Creates a new export job.

        Args:
        """

        url = "/api/v1/jobs/export"

        return self._client._invoke_async(Job, "POST", url, "application/json", parameters)

    def load_packages(self) -> Awaitable[Job]:
        """
        Creates a new load packages job.

        Args:
        """

        url = "/api/v1/jobs/load-packages"

        return self._client._invoke_async(Job, "POST", url, "application/json", None)

    def clear_cache(self, catalog_id: str, begin: datetime, end: datetime) -> Awaitable[Job]:
        """
        Clears the catalog cache for the specified period of time.

        Args:
            catalog_id: The catalog identifier.
            begin: Start date/time.
            end: End date/time.
        """

        url = "/api/v1/jobs/clear-cache"

        queryValues: dict[str, str] = {
            "catalogId": quote(_to_string(catalog_id), safe=""),
            "begin": quote(_to_string(begin), safe=""),
            "end": quote(_to_string(end), safe=""),
        }

        query: str = "?" + "&".join(f"{key}={value}" for (key, value) in queryValues.items())
        url += query

        return self._client._invoke_async(Job, "POST", url, "application/json", None)

    def get_jobs(self) -> Awaitable[list[Job]]:
        """
        Gets a list of jobs.

        Args:
        """

        url = "/api/v1/jobs"

        return self._client._invoke_async(list[Job], "GET", url, "application/json", None)

    def get_job_status(self, job_id: UUID) -> Awaitable[JobStatus]:
        """
        Gets the status of the specified job.

        Args:
            job_id: 
        """

        url = "/api/v1/jobs/{jobId}/status"
        url = url.replace("{jobId}", quote(str(job_id), safe=""))

        return self._client._invoke_async(JobStatus, "GET", url, "application/json", None)

    def delete_job(self, job_id: UUID) -> Awaitable[StreamResponse]:
        """
        Cancels the specified job.

        Args:
            job_id: 
        """

        url = "/api/v1/jobs/{jobId}"
        url = url.replace("{jobId}", quote(str(job_id), safe=""))

        return self._client._invoke_async(StreamResponse, "DELETE", url, "application/octet-stream", None)


class PackageReferencesClient:
    """Provides methods to interact with package references."""

    _client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self._client = client

    def get(self) -> Awaitable[dict[str, PackageReference]]:
        """
        Gets the list of package references.

        Args:
        """

        url = "/api/v1/packagereferences"

        return self._client._invoke_async(dict[str, PackageReference], "GET", url, "application/json", None)

    def put(self, package_reference_id: UUID, package_reference: PackageReference) -> Awaitable[None]:
        """
        Puts a package reference.

        Args:
            package_reference_id: The identifier of the package reference.
        """

        url = "/api/v1/packagereferences/{packageReferenceId}"
        url = url.replace("{packageReferenceId}", quote(str(package_reference_id), safe=""))

        return self._client._invoke_async(type(None), "PUT", url, "", package_reference)

    def delete(self, package_reference_id: UUID) -> Awaitable[None]:
        """
        Deletes a package reference.

        Args:
            package_reference_id: The ID of the package reference.
        """

        url = "/api/v1/packagereferences/{packageReferenceId}"
        url = url.replace("{packageReferenceId}", quote(str(package_reference_id), safe=""))

        return self._client._invoke_async(type(None), "DELETE", url, "", None)

    def get_versions(self, package_reference_id: UUID) -> Awaitable[list[str]]:
        """
        Gets package versions.

        Args:
            package_reference_id: The ID of the package reference.
        """

        url = "/api/v1/packagereferences/{packageReferenceId}/versions"
        url = url.replace("{packageReferenceId}", quote(str(package_reference_id), safe=""))

        return self._client._invoke_async(list[str], "GET", url, "application/json", None)


class SourcesClient:
    """Provides methods to interact with sources."""

    _client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self._client = client

    def get_descriptions(self) -> Awaitable[list[ExtensionDescription]]:
        """
        Gets the list of sources.

        Args:
        """

        url = "/api/v1/sources/descriptions"

        return self._client._invoke_async(list[ExtensionDescription], "GET", url, "application/json", None)

    def get_registrations(self) -> Awaitable[dict[str, DataSourceRegistration]]:
        """
        Gets the list of backend sources.

        Args:
        """

        url = "/api/v1/sources/registrations"

        return self._client._invoke_async(dict[str, DataSourceRegistration], "GET", url, "application/json", None)

    def put_registration(self, registration_id: UUID, registration: DataSourceRegistration) -> Awaitable[None]:
        """
        Puts a backend source.

        Args:
            registration_id: The identifier of the registration.
        """

        url = "/api/v1/sources/registrations/{registrationId}"
        url = url.replace("{registrationId}", quote(str(registration_id), safe=""))

        return self._client._invoke_async(type(None), "PUT", url, "", registration)

    def delete_registration(self, registration_id: UUID) -> Awaitable[None]:
        """
        Deletes a backend source.

        Args:
            registration_id: The identifier of the registration.
        """

        url = "/api/v1/sources/registrations/{registrationId}"
        url = url.replace("{registrationId}", quote(str(registration_id), safe=""))

        return self._client._invoke_async(type(None), "DELETE", url, "", None)


class UsersClient:
    """Provides methods to interact with users."""

    _client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self._client = client

    def get_authentication_schemes(self) -> Awaitable[list[AuthenticationSchemeDescription]]:
        """
        Returns a list of available authentication schemes.

        Args:
        """

        url = "/api/v1/users/authentication-schemes"

        return self._client._invoke_async(list[AuthenticationSchemeDescription], "GET", url, "application/json", None)

    def authenticate(self, scheme: str, return_url: str) -> Awaitable[StreamResponse]:
        """
        Authenticates the user.

        Args:
            scheme: The authentication scheme to challenge.
            return_url: The URL to return after successful authentication.
        """

        url = "/api/v1/users/authenticate"

        queryValues: dict[str, str] = {
            "scheme": quote(_to_string(scheme), safe=""),
            "returnUrl": quote(_to_string(return_url), safe=""),
        }

        query: str = "?" + "&".join(f"{key}={value}" for (key, value) in queryValues.items())
        url += query

        return self._client._invoke_async(StreamResponse, "GET", url, "application/octet-stream", None)

    def sign_out(self, return_url: str) -> Awaitable[StreamResponse]:
        """
        Logs out the user.

        Args:
            return_url: 
        """

        url = "/api/v1/users/signout"

        queryValues: dict[str, str] = {
            "returnUrl": quote(_to_string(return_url), safe=""),
        }

        query: str = "?" + "&".join(f"{key}={value}" for (key, value) in queryValues.items())
        url += query

        return self._client._invoke_async(StreamResponse, "GET", url, "application/octet-stream", None)

    def refresh_token(self, request: RefreshTokenRequest) -> Awaitable[TokenPair]:
        """
        Refreshes the JWT token.

        Args:
        """

        url = "/api/v1/users/refresh-token"

        return self._client._invoke_async(TokenPair, "POST", url, "application/json", request)

    def revoke_token(self, request: RevokeTokenRequest) -> Awaitable[StreamResponse]:
        """
        Revokes a refresh token.

        Args:
        """

        url = "/api/v1/users/revoke-token"

        return self._client._invoke_async(StreamResponse, "POST", url, "application/octet-stream", request)

    def get_me(self) -> Awaitable[NexusUser]:
        """
        Gets the current user.

        Args:
        """

        url = "/api/v1/users/me"

        return self._client._invoke_async(NexusUser, "GET", url, "application/json", None)

    def generate_refresh_token(self) -> Awaitable[str]:
        """
        Generates a refresh token.

        Args:
        """

        url = "/api/v1/users/generate-refresh-token"

        return self._client._invoke_async(str, "POST", url, "application/json", None)

    def get_users(self) -> Awaitable[list[NexusUser]]:
        """
        Gets a list of users.

        Args:
        """

        url = "/api/v1/users"

        return self._client._invoke_async(list[NexusUser], "GET", url, "application/json", None)

    def delete_user(self, user_id: str) -> Awaitable[StreamResponse]:
        """
        Deletes a user.

        Args:
            user_id: The identifier of the user.
        """

        url = "/api/v1/users/{userId}"
        url = url.replace("{userId}", quote(str(user_id), safe=""))

        return self._client._invoke_async(StreamResponse, "DELETE", url, "application/octet-stream", None)

    def put_claim(self, user_id: str, claim_id: UUID, claim: NexusClaim) -> Awaitable[StreamResponse]:
        """
        Puts a claim.

        Args:
            user_id: The identifier of the user.
            claim_id: The identifier of claim.
        """

        url = "/api/v1/users/{userId}/{claimId}"
        url = url.replace("{userId}", quote(str(user_id), safe=""))
        url = url.replace("{claimId}", quote(str(claim_id), safe=""))

        return self._client._invoke_async(StreamResponse, "PUT", url, "application/octet-stream", claim)

    def delete_claim(self, user_id: str, claim_id: UUID) -> Awaitable[StreamResponse]:
        """
        Deletes a claim.

        Args:
            user_id: The identifier of the user.
            claim_id: The identifier of the claim.
        """

        url = "/api/v1/users/{userId}/{claimId}"
        url = url.replace("{userId}", quote(str(user_id), safe=""))
        url = url.replace("{claimId}", quote(str(claim_id), safe=""))

        return self._client._invoke_async(StreamResponse, "DELETE", url, "application/octet-stream", None)


class WritersClient:
    """Provides methods to interact with writers."""

    _client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self._client = client

    def get_descriptions(self) -> Awaitable[list[ExtensionDescription]]:
        """
        Gets the list of writers.

        Args:
        """

        url = "/api/v1/writers/descriptions"

        return self._client._invoke_async(list[ExtensionDescription], "GET", url, "application/json", None)




class NexusAsyncClient:
    """A client for the Nexus system."""
    
    _nexus_configuration_header_key: str = "Nexus-Configuration"
    _authorization_header_key: str = "Authorization"

    _token_folder_path: str = os.path.join(str(Path.home()), ".nexusapi", "tokens")

    _token_pair: Optional[TokenPair]
    _http_client: AsyncClient
    _token_file_path: Optional[str]

    _artifacts: ArtifactsClient
    _catalogs: CatalogsClient
    _data: DataClient
    _jobs: JobsClient
    _packageReferences: PackageReferencesClient
    _sources: SourcesClient
    _users: UsersClient
    _writers: WritersClient


    @classmethod
    def create(cls, base_url: str) -> NexusAsyncClient:
        """
        Initializes a new instance of the NexusAsyncClient
        
            Args:
                base_url: The base URL to use.
        """
        return NexusAsyncClient(AsyncClient(base_url=base_url))

    def __init__(self, http_client: AsyncClient):
        """
        Initializes a new instance of the NexusAsyncClient
        
            Args:
                http_client: The HTTP client to use.
        """

        if http_client.base_url is None:
            raise Exception("The base url of the HTTP client must be set.")

        self._http_client = http_client
        self._token_pair = None

        self._artifacts = ArtifactsClient(self)
        self._catalogs = CatalogsClient(self)
        self._data = DataClient(self)
        self._jobs = JobsClient(self)
        self._packageReferences = PackageReferencesClient(self)
        self._sources = SourcesClient(self)
        self._users = UsersClient(self)
        self._writers = WritersClient(self)


    @property
    def is_authenticated(self) -> bool:
        """Gets a value which indicates if the user is authenticated."""
        return self._token_pair is not None

    @property
    def artifacts(self) -> ArtifactsClient:
        """Gets the ArtifactsClient."""
        return self._artifacts

    @property
    def catalogs(self) -> CatalogsClient:
        """Gets the CatalogsClient."""
        return self._catalogs

    @property
    def data(self) -> DataClient:
        """Gets the DataClient."""
        return self._data

    @property
    def jobs(self) -> JobsClient:
        """Gets the JobsClient."""
        return self._jobs

    @property
    def package_references(self) -> PackageReferencesClient:
        """Gets the PackageReferencesClient."""
        return self._packageReferences

    @property
    def sources(self) -> SourcesClient:
        """Gets the SourcesClient."""
        return self._sources

    @property
    def users(self) -> UsersClient:
        """Gets the UsersClient."""
        return self._users

    @property
    def writers(self) -> WritersClient:
        """Gets the WritersClient."""
        return self._writers



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
                actual_refresh_token = _decode(str, jsonObject)

        else:
            Path(self._token_folder_path).mkdir(parents=True, exist_ok=True)

            with open(self._token_file_path, "w") as json_file:
                json.dump(refresh_token, json_file, indent=4, cls=_MyEncoder)
                actual_refresh_token = refresh_token
                
        await self._refresh_token_async(actual_refresh_token)

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
                            await self._refresh_token_async(self._token_pair.refresh_token)

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
                return typing.cast(T, StreamResponse(response))

            else:

                jsonObject = json.loads(response.text)
                return_value = _decode(typeOfT, jsonObject)

                if return_value is None:
                    raise NexusException(f"N01", "Response data could not be deserialized.")

                return return_value

        finally:
            if typeOfT is not StreamResponse:
                await response.aclose()
    
    def _build_request_message(self, method: str, relative_url: str, http_content: Any, accept_header_value: str) -> Request:
       
        request_message = self._http_client.build_request(method, relative_url, content = http_content)
        request_message.headers["Content-Type"] = "application/json"

        if accept_header_value is not None:
            request_message.headers["Accept"] = accept_header_value

        return request_message

    async def _refresh_token_async(self, refresh_token):
        # see https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/blob/dev/src/Microsoft.IdentityModel.Tokens/Validators.cs#L390

        refresh_request = RefreshTokenRequest(refresh_token)
        token_pair = await self.users.refresh_token(refresh_request)

        if self._token_file_path is not None:
            Path(self._token_folder_path).mkdir(parents=True, exist_ok=True)
            
            with open(self._token_file_path, "w") as json_file:
                json.dump(token_pair.refresh_token, json_file, indent=4, cls=_MyEncoder)

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
    async def __aenter__(self) -> NexusAsyncClient:
        return self

    async def __aexit__(self, exc_type, exc_value, exc_traceback):
        if (self._http_client is not None):
            await self._http_client.aclose()

    # "extension" methods
    def attach_configuration2(self, *configuration: Tuple[str, str]) -> Any:
        """Attaches configuration data to subsequent Nexus API requests."""

        dict_configuration = { key: value for key, value in configuration }
        return self.attach_configuration(dict_configuration)
        
