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

@dataclass(frozen=True)
class ResourceCatalog:
    """
    A catalog is a top level element and holds a list of resources.

    Args:
        id: Gets the identifier.
        properties: Gets the properties.
        resources: Gets the list of representations.
    """

    id: str
    """Gets the identifier."""

    properties: Optional[object]
    """Gets the properties."""

    resources: Optional[list[Resource]]
    """Gets the list of representations."""


@dataclass(frozen=True)
class Resource:
    """
    A resource is part of a resource catalog and holds a list of representations.

    Args:
        id: Gets the identifier.
        properties: Gets the properties.
        representations: Gets the list of representations.
    """

    id: str
    """Gets the identifier."""

    properties: Optional[object]
    """Gets the properties."""

    representations: Optional[list[Representation]]
    """Gets the list of representations."""


@dataclass(frozen=True)
class Representation:
    """
    A representation is part of a resource.

    Args:
        data_type: The data type.
        sample_period: The sample period.
    """

    data_type: NexusDataType
    """The data type."""

    sample_period: timedelta
    """The sample period."""


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


@dataclass(frozen=True)
class CatalogInfo:
    """
    A structure for catalog information.

    Args:
        id: The identifier.
        title: The title.
        contact: A nullable contact.
        readme: A nullable readme.
        license: A nullable license.
        is_readable: A boolean which indicates if the catalog is accessible.
        is_writable: A boolean which indicates if the catalog is editable.
        is_released: A boolean which indicates if the catalog is released.
        is_visible: A boolean which indicates if the catalog is visible.
        is_owner: A boolean which indicates if the catalog is owned by the current user.
        data_source_info_url: A nullable info URL of the data source.
        data_source_type: The data source type.
        data_source_registration_id: The data source registration identifier.
        package_reference_id: The package reference identifier.
    """

    id: str
    """The identifier."""

    title: str
    """The title."""

    contact: Optional[str]
    """A nullable contact."""

    readme: Optional[str]
    """A nullable readme."""

    license: Optional[str]
    """A nullable license."""

    is_readable: bool
    """A boolean which indicates if the catalog is accessible."""

    is_writable: bool
    """A boolean which indicates if the catalog is editable."""

    is_released: bool
    """A boolean which indicates if the catalog is released."""

    is_visible: bool
    """A boolean which indicates if the catalog is visible."""

    is_owner: bool
    """A boolean which indicates if the catalog is owned by the current user."""

    data_source_info_url: Optional[str]
    """A nullable info URL of the data source."""

    data_source_type: str
    """The data source type."""

    data_source_registration_id: UUID
    """The data source registration identifier."""

    package_reference_id: UUID
    """The package reference identifier."""


@dataclass(frozen=True)
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


@dataclass(frozen=True)
class CatalogAvailability:
    """
    The catalog availability.

    Args:
        data: The actual availability data.
    """

    data: list[float]
    """The actual availability data."""


@dataclass(frozen=True)
class CatalogMetadata:
    """
    A structure for catalog metadata.

    Args:
        contact: The contact.
        group_memberships: A list of groups the catalog is part of.
        overrides: Overrides for the catalog.
    """

    contact: Optional[str]
    """The contact."""

    group_memberships: Optional[list[str]]
    """A list of groups the catalog is part of."""

    overrides: Optional[ResourceCatalog]
    """Overrides for the catalog."""


@dataclass(frozen=True)
class Job:
    """
    Description of a job.

    Args:
        id: The global unique identifier.
        type: The job type
        owner: The owner of the job.
        parameters: The job parameters.
    """

    id: UUID
    """The global unique identifier."""

    type: str
    """The job type"""

    owner: str
    """The owner of the job."""

    parameters: Optional[object]
    """The job parameters."""


@dataclass(frozen=True)
class JobStatus:
    """
    Describes the status of the job.

    Args:
        start: The start date/time.
        status: The status.
        progress: The progress from 0 to 1.
        exception_message: The nullable exception message.
        result: The nullable result.
    """

    start: datetime
    """The start date/time."""

    status: TaskStatus
    """The status."""

    progress: float
    """The progress from 0 to 1."""

    exception_message: Optional[str]
    """The nullable exception message."""

    result: Optional[object]
    """The nullable result."""


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


@dataclass(frozen=True)
class ExportParameters:
    """
    A structure for export parameters.

    Args:
        begin: The start date/time.
        end: The end date/time.
        file_period: The file period.
        type: The writer type.
        resource_paths: The resource paths to export.
        configuration: The configuration.
    """

    begin: datetime
    """The start date/time."""

    end: datetime
    """The end date/time."""

    file_period: timedelta
    """The file period."""

    type: str
    """The writer type."""

    resource_paths: list[str]
    """The resource paths to export."""

    configuration: Optional[object]
    """The configuration."""


@dataclass(frozen=True)
class PackageReference:
    """
    A package reference.

    Args:
        id: The unique identifier of the package reference.
        provider: The provider which loads the package.
        configuration: The configuration of the package reference.
    """

    id: UUID
    """The unique identifier of the package reference."""

    provider: str
    """The provider which loads the package."""

    configuration: dict[str, str]
    """The configuration of the package reference."""


@dataclass(frozen=True)
class ExtensionDescription:
    """
    An extension description.

    Args:
        type: The extension type.
        description: A nullable description.
        project_url: A nullable project website URL.
        repository_url: A nullable source repository URL.
        additional_info: A nullable dictionary with additional information.
    """

    type: str
    """The extension type."""

    description: Optional[str]
    """A nullable description."""

    project_url: Optional[str]
    """A nullable project website URL."""

    repository_url: Optional[str]
    """A nullable source repository URL."""

    additional_info: Optional[dict[str, str]]
    """A nullable dictionary with additional information."""


@dataclass(frozen=True)
class DataSourceRegistration:
    """
    A data source registration.

    Args:
        id: The unique identifier of the data source registration.
        type: The type of the data source.
        resource_locator: An URL which points to the data.
        configuration: Configuration parameters for the instantiated source.
        info_url: An optional info URL.
        release_pattern: An optional regular expressions pattern to select the catalogs to be released. By default, all catalogs will be released.
        visibility_pattern: An optional regular expressions pattern to select the catalogs to be visible. By default, all catalogs will be visible.
    """

    id: UUID
    """The unique identifier of the data source registration."""

    type: str
    """The type of the data source."""

    resource_locator: str
    """An URL which points to the data."""

    configuration: Optional[object]
    """Configuration parameters for the instantiated source."""

    info_url: Optional[str]
    """An optional info URL."""

    release_pattern: str
    """An optional regular expressions pattern to select the catalogs to be released. By default, all catalogs will be released."""

    visibility_pattern: str
    """An optional regular expressions pattern to select the catalogs to be visible. By default, all catalogs will be visible."""


@dataclass(frozen=True)
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


@dataclass(frozen=True)
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


@dataclass(frozen=True)
class RefreshTokenRequest:
    """
    A refresh token request.

    Args:
        refresh_token: The refresh token.
    """

    refresh_token: str
    """The refresh token."""


@dataclass(frozen=True)
class RevokeTokenRequest:
    """
    A revoke token request.

    Args:
        token: The refresh token.
    """

    token: str
    """The refresh token."""


@dataclass(frozen=True)
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


@dataclass(frozen=True)
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


@dataclass(frozen=True)
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

        return self._client._invoke(StreamResponse, "GET", url, "application/octet-stream", None, None)


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

        return self._client._invoke(ResourceCatalog, "GET", url, "application/json", None, None)

    def get_child_catalog_infos(self, catalog_id: str) -> Awaitable[list[CatalogInfo]]:
        """
        Gets a list of child catalog info for the provided parent catalog identifier.

        Args:
            catalog_id: The parent catalog identifier.
        """

        url = "/api/v1/catalogs/{catalogId}/child-catalog-infos"
        url = url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self._client._invoke(list[CatalogInfo], "GET", url, "application/json", None, None)

    def get_time_range(self, catalog_id: str) -> Awaitable[CatalogTimeRange]:
        """
        Gets the specified catalog's time range.

        Args:
            catalog_id: The catalog identifier.
        """

        url = "/api/v1/catalogs/{catalogId}/timerange"
        url = url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self._client._invoke(CatalogTimeRange, "GET", url, "application/json", None, None)

    def get_availability(self, catalog_id: str, begin: datetime, end: datetime, step: timedelta) -> Awaitable[CatalogAvailability]:
        """
        Gets the specified catalog availability.

        Args:
            catalog_id: The catalog identifier.
            begin: Start date/time.
            end: End date/time.
            step: Step period.
        """

        url = "/api/v1/catalogs/{catalogId}/availability"
        url = url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        queryValues: dict[str, str] = {
            "begin": quote(_to_string(begin), safe=""),
            "end": quote(_to_string(end), safe=""),
            "step": quote(_to_string(step), safe=""),
        }

        query: str = "?" + "&".join(f"{key}={value}" for (key, value) in queryValues.items())
        url += query

        return self._client._invoke(CatalogAvailability, "GET", url, "application/json", None, None)

    def get_attachments(self, catalog_id: str) -> Awaitable[list[str]]:
        """
        Gets all attachments for the specified catalog.

        Args:
            catalog_id: The catalog identifier.
        """

        url = "/api/v1/catalogs/{catalogId}/attachments"
        url = url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self._client._invoke(list[str], "GET", url, "application/json", None, None)

    def upload_attachment(self, catalog_id: str, attachment_id: str, content: Union[bytes, Iterable[bytes], AsyncIterable[bytes]]) -> Awaitable[StreamResponse]:
        """
        Uploads the specified attachment.

        Args:
            catalog_id: The catalog identifier.
            attachment_id: The attachment identifier.
        """

        url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}"
        url = url.replace("{catalogId}", quote(str(catalog_id), safe=""))
        url = url.replace("{attachmentId}", quote(str(attachment_id), safe=""))

        return self._client._invoke(StreamResponse, "PUT", url, "application/octet-stream", "application/octet-stream", content)

    def delete_attachment(self, catalog_id: str, attachment_id: str) -> Awaitable[StreamResponse]:
        """
        Deletes the specified attachment.

        Args:
            catalog_id: The catalog identifier.
            attachment_id: The attachment identifier.
        """

        url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}"
        url = url.replace("{catalogId}", quote(str(catalog_id), safe=""))
        url = url.replace("{attachmentId}", quote(str(attachment_id), safe=""))

        return self._client._invoke(StreamResponse, "DELETE", url, "application/octet-stream", None, None)

    def get_attachment_stream(self, catalog_id: str, attachment_id: str) -> Awaitable[StreamResponse]:
        """
        Gets the specified attachment.

        Args:
            catalog_id: The catalog identifier.
            attachment_id: The attachment identifier.
        """

        url = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}/content"
        url = url.replace("{catalogId}", quote(str(catalog_id), safe=""))
        url = url.replace("{attachmentId}", quote(str(attachment_id), safe=""))

        return self._client._invoke(StreamResponse, "GET", url, "application/octet-stream", None, None)

    def get_metadata(self, catalog_id: str) -> Awaitable[CatalogMetadata]:
        """
        Gets the catalog metadata.

        Args:
            catalog_id: The catalog identifier.
        """

        url = "/api/v1/catalogs/{catalogId}/metadata"
        url = url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self._client._invoke(CatalogMetadata, "GET", url, "application/json", None, None)

    def set_metadata(self, catalog_id: str, catalog_metadata: CatalogMetadata) -> Awaitable[None]:
        """
        Puts the catalog metadata.

        Args:
            catalog_id: The catalog identifier.
        """

        url = "/api/v1/catalogs/{catalogId}/metadata"
        url = url.replace("{catalogId}", quote(str(catalog_id), safe=""))

        return self._client._invoke(type(None), "PUT", url, "", "application/json", json.dumps(JsonEncoder.encode(catalog_metadata, _json_encoder_options)))


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

        return self._client._invoke(StreamResponse, "GET", url, "application/octet-stream", None, None)


class JobsClient:
    """Provides methods to interact with jobs."""

    _client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self._client = client

    def get_jobs(self) -> Awaitable[list[Job]]:
        """
        Gets a list of jobs.

        Args:
        """

        url = "/api/v1/jobs"

        return self._client._invoke(list[Job], "GET", url, "application/json", None, None)

    def cancel_job(self, job_id: UUID) -> Awaitable[StreamResponse]:
        """
        Cancels the specified job.

        Args:
            job_id: 
        """

        url = "/api/v1/jobs/{jobId}"
        url = url.replace("{jobId}", quote(str(job_id), safe=""))

        return self._client._invoke(StreamResponse, "DELETE", url, "application/octet-stream", None, None)

    def get_job_status(self, job_id: UUID) -> Awaitable[JobStatus]:
        """
        Gets the status of the specified job.

        Args:
            job_id: 
        """

        url = "/api/v1/jobs/{jobId}/status"
        url = url.replace("{jobId}", quote(str(job_id), safe=""))

        return self._client._invoke(JobStatus, "GET", url, "application/json", None, None)

    def export(self, parameters: ExportParameters) -> Awaitable[Job]:
        """
        Creates a new export job.

        Args:
        """

        url = "/api/v1/jobs/export"

        return self._client._invoke(Job, "POST", url, "application/json", "application/json", json.dumps(JsonEncoder.encode(parameters, _json_encoder_options)))

    def load_packages(self) -> Awaitable[Job]:
        """
        Creates a new load packages job.

        Args:
        """

        url = "/api/v1/jobs/load-packages"

        return self._client._invoke(Job, "POST", url, "application/json", None, None)

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

        return self._client._invoke(Job, "POST", url, "application/json", None, None)


class PackageReferencesClient:
    """Provides methods to interact with package references."""

    _client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self._client = client

    def get(self) -> Awaitable[list[PackageReference]]:
        """
        Gets the list of package references.

        Args:
        """

        url = "/api/v1/packagereferences"

        return self._client._invoke(list[PackageReference], "GET", url, "application/json", None, None)

    def set(self, package_reference: PackageReference) -> Awaitable[None]:
        """
        Puts a package reference.

        Args:
        """

        url = "/api/v1/packagereferences"

        return self._client._invoke(type(None), "PUT", url, "", "application/json", json.dumps(JsonEncoder.encode(package_reference, _json_encoder_options)))

    def delete(self, package_reference_id: UUID) -> Awaitable[None]:
        """
        Deletes a package reference.

        Args:
            package_reference_id: The ID of the package reference.
        """

        url = "/api/v1/packagereferences/{packageReferenceId}"
        url = url.replace("{packageReferenceId}", quote(str(package_reference_id), safe=""))

        return self._client._invoke(type(None), "DELETE", url, "", None, None)

    def get_versions(self, package_reference_id: UUID) -> Awaitable[list[str]]:
        """
        Gets package versions.

        Args:
            package_reference_id: The ID of the package reference.
        """

        url = "/api/v1/packagereferences/{packageReferenceId}/versions"
        url = url.replace("{packageReferenceId}", quote(str(package_reference_id), safe=""))

        return self._client._invoke(list[str], "GET", url, "application/json", None, None)


class SourcesClient:
    """Provides methods to interact with sources."""

    _client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self._client = client

    def get_descriptions(self) -> Awaitable[list[ExtensionDescription]]:
        """
        Gets the list of source descriptions.

        Args:
        """

        url = "/api/v1/sources/descriptions"

        return self._client._invoke(list[ExtensionDescription], "GET", url, "application/json", None, None)

    def get_registrations(self, username: Optional[str] = None) -> Awaitable[list[DataSourceRegistration]]:
        """
        Gets the list of backend sources.

        Args:
            username: The optional username. If not specified, the name of the current user will be used.
        """

        url = "/api/v1/sources/registrations"

        queryValues: dict[str, str] = {
            "username": quote(_to_string(username), safe=""),
        }

        query: str = "?" + "&".join(f"{key}={value}" for (key, value) in queryValues.items())
        url += query

        return self._client._invoke(list[DataSourceRegistration], "GET", url, "application/json", None, None)

    def set_registration(self, registration: DataSourceRegistration, username: Optional[str] = None) -> Awaitable[StreamResponse]:
        """
        Puts a backend source.

        Args:
            username: The optional username. If not specified, the name of the current user will be used.
        """

        url = "/api/v1/sources/registrations"

        queryValues: dict[str, str] = {
            "username": quote(_to_string(username), safe=""),
        }

        query: str = "?" + "&".join(f"{key}={value}" for (key, value) in queryValues.items())
        url += query

        return self._client._invoke(StreamResponse, "PUT", url, "application/octet-stream", "application/json", json.dumps(JsonEncoder.encode(registration, _json_encoder_options)))

    def delete_registration(self, registration_id: UUID, username: Optional[str] = None) -> Awaitable[StreamResponse]:
        """
        Deletes a backend source.

        Args:
            registration_id: The identifier of the registration.
            username: The optional username. If not specified, the name of the current user will be used.
        """

        url = "/api/v1/sources/registrations/{registrationId}"
        url = url.replace("{registrationId}", quote(str(registration_id), safe=""))

        queryValues: dict[str, str] = {
            "username": quote(_to_string(username), safe=""),
        }

        query: str = "?" + "&".join(f"{key}={value}" for (key, value) in queryValues.items())
        url += query

        return self._client._invoke(StreamResponse, "DELETE", url, "application/octet-stream", None, None)


class SystemClient:
    """Provides methods to interact with system."""

    _client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self._client = client

    def get_help_link(self) -> Awaitable[str]:
        """
        Gets the configured help link.

        Args:
        """

        url = "/api/v1/system/help-link"

        return self._client._invoke(str, "GET", url, "application/json", None, None)

    def get_configuration(self) -> Awaitable[Optional[object]]:
        """
        Gets the system configuration.

        Args:
        """

        url = "/api/v1/system/configuration"

        return self._client._invoke(Optional[object], "GET", url, "application/json", None, None)

    def set_configuration(self, configuration: object) -> Awaitable[None]:
        """
        Sets the system configuration.

        Args:
        """

        url = "/api/v1/system/configuration"

        return self._client._invoke(type(None), "PUT", url, "", "application/json", json.dumps(JsonEncoder.encode(configuration, _json_encoder_options)))


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

        return self._client._invoke(list[AuthenticationSchemeDescription], "GET", url, "application/json", None, None)

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

        return self._client._invoke(StreamResponse, "POST", url, "application/octet-stream", None, None)

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

        return self._client._invoke(StreamResponse, "POST", url, "application/octet-stream", None, None)

    def refresh_token(self, request: RefreshTokenRequest) -> Awaitable[TokenPair]:
        """
        Refreshes the JWT token.

        Args:
        """

        url = "/api/v1/users/refresh-token"

        return self._client._invoke(TokenPair, "POST", url, "application/json", "application/json", json.dumps(JsonEncoder.encode(request, _json_encoder_options)))

    def revoke_token(self, request: RevokeTokenRequest) -> Awaitable[StreamResponse]:
        """
        Revokes a refresh token.

        Args:
        """

        url = "/api/v1/users/revoke-token"

        return self._client._invoke(StreamResponse, "POST", url, "application/octet-stream", "application/json", json.dumps(JsonEncoder.encode(request, _json_encoder_options)))

    def get_me(self) -> Awaitable[NexusUser]:
        """
        Gets the current user.

        Args:
        """

        url = "/api/v1/users/me"

        return self._client._invoke(NexusUser, "GET", url, "application/json", None, None)

    def generate_refresh_token(self) -> Awaitable[str]:
        """
        Generates a refresh token.

        Args:
        """

        url = "/api/v1/users/generate-refresh-token"

        return self._client._invoke(str, "POST", url, "application/json", None, None)

    def accept_license(self, catalog_id: str) -> Awaitable[StreamResponse]:
        """
        Accepts the license of the specified catalog.

        Args:
            catalog_id: The catalog identifier.
        """

        url = "/api/v1/users/accept-license"

        queryValues: dict[str, str] = {
            "catalogId": quote(_to_string(catalog_id), safe=""),
        }

        query: str = "?" + "&".join(f"{key}={value}" for (key, value) in queryValues.items())
        url += query

        return self._client._invoke(StreamResponse, "GET", url, "application/octet-stream", None, None)

    def get_users(self) -> Awaitable[list[NexusUser]]:
        """
        Gets a list of users.

        Args:
        """

        url = "/api/v1/users"

        return self._client._invoke(list[NexusUser], "GET", url, "application/json", None, None)

    def delete_user(self, user_id: str) -> Awaitable[StreamResponse]:
        """
        Deletes a user.

        Args:
            user_id: The identifier of the user.
        """

        url = "/api/v1/users/{userId}"
        url = url.replace("{userId}", quote(str(user_id), safe=""))

        return self._client._invoke(StreamResponse, "DELETE", url, "application/octet-stream", None, None)

    def set_claim(self, user_id: str, claim_id: UUID, claim: NexusClaim) -> Awaitable[StreamResponse]:
        """
        Puts a claim.

        Args:
            user_id: The identifier of the user.
            claim_id: The identifier of claim.
        """

        url = "/api/v1/users/{userId}/{claimId}"
        url = url.replace("{userId}", quote(str(user_id), safe=""))
        url = url.replace("{claimId}", quote(str(claim_id), safe=""))

        return self._client._invoke(StreamResponse, "PUT", url, "application/octet-stream", "application/json", json.dumps(JsonEncoder.encode(claim, _json_encoder_options)))

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

        return self._client._invoke(StreamResponse, "DELETE", url, "application/octet-stream", None, None)


class WritersClient:
    """Provides methods to interact with writers."""

    _client: NexusAsyncClient
    
    def __init__(self, client: NexusAsyncClient):
        self._client = client

    def get_descriptions(self) -> Awaitable[list[ExtensionDescription]]:
        """
        Gets the list of writer descriptions.

        Args:
        """

        url = "/api/v1/writers/descriptions"

        return self._client._invoke(list[ExtensionDescription], "GET", url, "application/json", None, None)




class NexusAsyncClient:
    """A client for the Nexus system."""
    
    _nexus_configuration_header_key: str = "Nexus-Configuration"
    _authorization_header_key: str = "Authorization"

    _token_folder_path: str = os.path.join(str(Path.home()), ".nexus-api", "tokens")

    _token_pair: Optional[TokenPair]
    _http_client: AsyncClient
    _token_file_path: Optional[str]

    _artifacts: ArtifactsClient
    _catalogs: CatalogsClient
    _data: DataClient
    _jobs: JobsClient
    _packageReferences: PackageReferencesClient
    _sources: SourcesClient
    _system: SystemClient
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
        self._system = SystemClient(self)
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
    def system(self) -> SystemClient:
        """Gets the SystemClient."""
        return self._system

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

    async def _refresh_token(self, refresh_token):
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
    async def __aenter__(self) -> NexusAsyncClient:
        return self

    async def __aexit__(self, exc_type, exc_value, exc_traceback):
        if (self._http_client is not None):
            await self._http_client.aclose()
