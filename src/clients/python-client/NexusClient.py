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

class NexusException(Exception):
    """A NexusException."""

    def __init__(self, status_code: str, message: str):
        self.status_code = status_code
        self.message = message

    status_code: str
    """The exception status code."""

    message: str
    """The exception message."""

@dataclass
class ResourceCatalog:
    """A catalog is a top level element and holds a list of resources.
    Args:
        id: Gets the identifier.
        properties: Gets the map of properties.
        resources: Gets the list of representations.
    """
    id: str
    properties: Dict[str, str]
    resources: List[Resource]

@dataclass
class Resource:
    """A resource is part of a resource catalog and holds a list of representations.
    Args:
        id: Gets the identifier.
        properties: Gets the map of properties.
        representations: Gets the list of representations.
    """
    id: str
    properties: Dict[str, str]
    representations: List[Representation]

@dataclass
class Representation:
    """A representation is part of a resource.
    Args:
        data_type: Gets the data type.
        sample_period: Gets the sample period.
        detail: Gets the detail.
        is_primary: Gets a value which indicates the primary representation to be used for aggregations. The value of this property is only relevant for resources with multiple representations.
    """
    data_type: NexusDataType
    sample_period: timedelta
    detail: str
    is_primary: bool

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


@dataclass
class CatalogTimeRange:
    """A catalog time range.
    Args:
        begin: The date/time of the first data in the catalog.
        end: The date/time of the last data in the catalog.
    """
    begin: datetime
    end: datetime

@dataclass
class CatalogAvailability:
    """The catalog availability.
    Args:
        data: The actual availability data.
    """
    data: Dict[str, float]

@dataclass
class CatalogMetadata:
    """A structure for catalog metadata.
    Args:
        contact: The contact.
        is_hidden: A boolean which indicates if the catalog should be hidden.
        group_memberships: A list of groups the catalog is part of.
        overrides: Overrides for the catalog.
    """
    contact: str
    is_hidden: bool
    group_memberships: List[str]
    overrides: ResourceCatalog

@dataclass
class Job:
    """Description of a job.
    Args:
        id: 06f8eb30-5924-4a71-bdff-322f92343f5b
        type: export
        owner: test@nexus.localhost
        parameters: Job parameters.
    """
    id: uuid
    type: str
    owner: str
    parameters: object

@dataclass
class ExportParameters:
    """A structure for export parameters.
    Args:
        begin: 2020-02-01T00:00:00Z
        end: 2020-02-02T00:00:00Z
        file_period: 00:00:00
        type: Nexus.Writers.Csv
        resource_paths: ["/IN_MEMORY/TEST/ACCESSIBLE/T1/1_s_mean", "/IN_MEMORY/TEST/ACCESSIBLE/V1/1_s_mean"]
        configuration: { "RowIndexFormat": "Index", "SignificantFigures": "4" }
    """
    begin: datetime
    end: datetime
    file_period: timedelta
    type: str
    resource_paths: List[str]
    configuration: Dict[str, str]

@dataclass
class JobStatus:
    """Describes the status of the job.
    Args:
        start: The start date/time.
        status: The status.
        progress: The progress from 0 to 1.
        exception_message: An optional exception message.
        result: The optional result.
    """
    start: datetime
    status: TaskStatus
    progress: float
    exception_message: str
    result: object

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
    """A package reference.
    Args:
        provider: The provider which loads the package.
        configuration: The configuration of the package reference.
    """
    provider: str
    configuration: Dict[str, str]

@dataclass
class ExtensionDescription:
    """An extension description.
    Args:
        type: The extension type.
        description: An optional description.
    """
    type: str
    description: str

@dataclass
class DataSourceRegistration:
    """A backend source.
    Args:
        type: The type of the backend source.
        resource_locator: An URL which points to the data.
        configuration: Configuration parameters for the instantiated source.
        publish: A boolean which indicates if the found catalogs should be available for everyone.
        disable: A boolean which indicates if this backend source should be ignored.
    """
    type: str
    resource_locator: str
    configuration: Dict[str, str]
    publish: bool
    disable: bool

@dataclass
class AuthenticationSchemeDescription:
    """Describes an OpenID connect provider.
    Args:
        scheme: The scheme.
        display_name: The display name.
    """
    scheme: str
    display_name: str

@dataclass
class TokenPair:
    """A token pair.
    Args:
        access_token: The JWT token.
        refresh_token: The refresh token.
    """
    access_token: str
    refresh_token: str

@dataclass
class RefreshTokenRequest:
    """A refresh token request.
    Args:
        refresh_token: The refresh token.
    """
    refresh_token: str

@dataclass
class RevokeTokenRequest:
    """A revoke token request.
    Args:
        token: The refresh token.
    """
    token: str

@dataclass
class NexusUser:
    """Represents a user.
    Args:
        id: The user identifier.
        name: The user name.
        refresh_tokens: The list of refresh tokens.
        claims: The map of claims.
    """
    id: str
    name: str
    refresh_tokens: List[RefreshToken]
    claims: Dict[str, NexusClaim]

@dataclass
class RefreshToken:
    """A refresh token.
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
    created: datetime
    expires: datetime
    revoked: datetime
    replaced_by_token: str
    is_expired: bool
    is_revoked: bool
    is_active: bool

@dataclass
class NexusClaim:
    """Represents a claim.
    Args:
        type: The claim type.
        value: The claim value.
    """
    type: str
    value: str


class ArtifactsClient:
    """Provides methods to interact with artifacts."""

    _client: NexusClient
    
    def __init__(self, client: NexusClient):
        self._client = client

    def download_artifact_async(self, artifact_id: str) -> Awaitable[StreamResponse]:
        """Gets the specified artifact."""
        
        url: str = "/api/v1/artifacts/{artifactId}"
        url = url.replace("{artifact_id}", quote(str(artifact_id)))

        return self._client.invoke_async(type(StreamResponse), "GET", url, "application/octet-stream", None)


class CatalogsClient:
    """Provides methods to interact with catalogs."""

    _client: NexusClient
    
    def __init__(self, client: NexusClient):
        self._client = client

    def get_catalog_async(self, catalog_id: str) -> Awaitable[ResourceCatalog]:
        """Gets the specified catalog."""
        
        url: str = "/api/v1/catalogs/{catalogId}"
        url = url.replace("{catalog_id}", quote(str(catalog_id)))

        return self._client.invoke_async(type(ResourceCatalog), "GET", url, "application/json", None)

    def get_child_catalog_ids_async(self, catalog_id: str) -> Awaitable[List[str]]:
        """Gets a list of child catalog identifiers for the provided parent catalog identifier."""
        
        url: str = "/api/v1/catalogs/{catalogId}/child-catalog-ids"
        url = url.replace("{catalog_id}", quote(str(catalog_id)))

        return self._client.invoke_async(type(List[str]), "GET", url, "application/json", None)

    def get_time_range_async(self, catalog_id: str) -> Awaitable[CatalogTimeRange]:
        """Gets the specified catalog's time range."""
        
        url: str = "/api/v1/catalogs/{catalogId}/timerange"
        url = url.replace("{catalog_id}", quote(str(catalog_id)))

        return self._client.invoke_async(type(CatalogTimeRange), "GET", url, "application/json", None)

    def get_catalog_availability_async(self, catalog_id: str, begin: datetime, end: datetime) -> Awaitable[CatalogAvailability]:
        """Gets the specified catalog availability."""
        
        url: str = "/api/v1/catalogs/{catalogId}/availability"
        url = url.replace("{catalog_id}", quote(str(catalog_id)))

        queryValues: Dict[str, str] = {
            "begin": quote(str(begin)),
            "end": quote(str(end)),
        }

        query: str = "?" + "&".join(f"{key}={value}" for (key, value) in queryValues.items())
        url += query

        return self._client.invoke_async(type(CatalogAvailability), "GET", url, "application/json", None)

    def download_attachement_async(self, catalog_id: str, attachment_id: str) -> Awaitable[StreamResponse]:
        """Gets the specified attachment."""
        
        url: str = "/api/v1/catalogs/{catalogId}/attachments/{attachmentId}/content"
        url = url.replace("{catalog_id}", quote(str(catalog_id)))
        url = url.replace("{attachment_id}", quote(str(attachment_id)))

        return self._client.invoke_async(type(StreamResponse), "GET", url, "application/octet-stream", None)

    def get_catalog_metadata_async(self, catalog_id: str) -> Awaitable[CatalogMetadata]:
        """Gets the catalog metadata."""
        
        url: str = "/api/v1/catalogs/{catalogId}/metadata"
        url = url.replace("{catalog_id}", quote(str(catalog_id)))

        return self._client.invoke_async(type(CatalogMetadata), "GET", url, "application/json", None)

    def put_catalog_metadata_async(self, catalog_id: str, catalog_metadata: CatalogMetadata) -> Awaitable[None]:
        """Puts the catalog metadata."""
        
        url: str = "/api/v1/catalogs/{catalogId}/metadata"
        url = url.replace("{catalog_id}", quote(str(catalog_id)))

        return self._client.invoke_async(type(object), "PUT", url, "", catalog_metadata)


class DataClient:
    """Provides methods to interact with data."""

    _client: NexusClient
    
    def __init__(self, client: NexusClient):
        self._client = client

    def get_stream_async(self, catalog_id: str, resource_id: str, representation_id: str, begin: datetime, end: datetime) -> Awaitable[StreamResponse]:
        """Gets the requested data."""
        
        url: str = "/api/v1/data"

        queryValues: Dict[str, str] = {
            "catalog_id": quote(str(catalog_id)),
            "resource_id": quote(str(resource_id)),
            "representation_id": quote(str(representation_id)),
            "begin": quote(str(begin)),
            "end": quote(str(end)),
        }

        query: str = "?" + "&".join(f"{key}={value}" for (key, value) in queryValues.items())
        url += query

        return self._client.invoke_async(type(StreamResponse), "GET", url, "application/octet-stream", None)


class JobsClient:
    """Provides methods to interact with jobs."""

    _client: NexusClient
    
    def __init__(self, client: NexusClient):
        self._client = client

    def export_async(self, parameters: ExportParameters) -> Awaitable[Job]:
        """Creates a new export job."""
        
        url: str = "/api/v1/jobs/export"

        return self._client.invoke_async(type(Job), "POST", url, "application/json", parameters)

    def load_packages_async(self) -> Awaitable[Job]:
        """Creates a new load packages job."""
        
        url: str = "/api/v1/jobs/load-packages"

        return self._client.invoke_async(type(Job), "POST", url, "application/json", None)

    def get_jobs_async(self) -> Awaitable[List[Job]]:
        """Gets a list of jobs."""
        
        url: str = "/api/v1/jobs"

        return self._client.invoke_async(type(List[Job]), "GET", url, "application/json", None)

    def get_job_status_async(self, job_id: uuid) -> Awaitable[JobStatus]:
        """Gets the status of the specified job."""
        
        url: str = "/api/v1/jobs/{jobId}/status"
        url = url.replace("{job_id}", quote(str(job_id)))

        return self._client.invoke_async(type(JobStatus), "GET", url, "application/json", None)

    def delete_job_async(self, job_id: uuid) -> Awaitable[StreamResponse]:
        """Cancels the specified job."""
        
        url: str = "/api/v1/jobs/{jobId}"
        url = url.replace("{job_id}", quote(str(job_id)))

        return self._client.invoke_async(type(StreamResponse), "DELETE", url, "application/octet-stream", None)


class PackageReferencesClient:
    """Provides methods to interact with package references."""

    _client: NexusClient
    
    def __init__(self, client: NexusClient):
        self._client = client

    def get_package_references_async(self) -> Awaitable[Dict[str, PackageReference]]:
        """Gets the list of package references."""
        
        url: str = "/api/v1/packagereferences"

        return self._client.invoke_async(type(Dict[str, PackageReference]), "GET", url, "application/json", None)

    def put_package_references_async(self, package_reference_id: uuid, package_reference: PackageReference) -> Awaitable[None]:
        """Puts a package reference."""
        
        url: str = "/api/v1/packagereferences/{packageReferenceId}"
        url = url.replace("{package_reference_id}", quote(str(package_reference_id)))

        return self._client.invoke_async(type(object), "PUT", url, "", package_reference)

    def delete_package_references_async(self, package_reference_id: uuid) -> Awaitable[None]:
        """Deletes a package reference."""
        
        url: str = "/api/v1/packagereferences/{packageReferenceId}"
        url = url.replace("{package_reference_id}", quote(str(package_reference_id)))

        return self._client.invoke_async(type(object), "DELETE", url, "", None)

    def get_package_versions_async(self, package_reference_id: uuid) -> Awaitable[List[str]]:
        """Gets package versions."""
        
        url: str = "/api/v1/packagereferences/{packageReferenceId}/versions"
        url = url.replace("{package_reference_id}", quote(str(package_reference_id)))

        return self._client.invoke_async(type(List[str]), "GET", url, "application/json", None)


class SourcesClient:
    """Provides methods to interact with sources."""

    _client: NexusClient
    
    def __init__(self, client: NexusClient):
        self._client = client

    def get_source_descriptions_async(self) -> Awaitable[List[ExtensionDescription]]:
        """Gets the list of sources."""
        
        url: str = "/api/v1/sources/descriptions"

        return self._client.invoke_async(type(List[ExtensionDescription]), "GET", url, "application/json", None)

    def get_source_registrations_async(self) -> Awaitable[Dict[str, DataSourceRegistration]]:
        """Gets the list of backend sources."""
        
        url: str = "/api/v1/sources/registrations"

        return self._client.invoke_async(type(Dict[str, DataSourceRegistration]), "GET", url, "application/json", None)

    def put_source_registration_async(self, registration_id: uuid, registration: DataSourceRegistration) -> Awaitable[None]:
        """Puts a backend source."""
        
        url: str = "/api/v1/sources/registrations/{registrationId}"
        url = url.replace("{registration_id}", quote(str(registration_id)))

        return self._client.invoke_async(type(object), "PUT", url, "", registration)

    def delete_source_registration_async(self, registration_id: uuid) -> Awaitable[None]:
        """Deletes a backend source."""
        
        url: str = "/api/v1/sources/registrations/{registrationId}"
        url = url.replace("{registration_id}", quote(str(registration_id)))

        return self._client.invoke_async(type(object), "DELETE", url, "", None)


class UsersClient:
    """Provides methods to interact with users."""

    _client: NexusClient
    
    def __init__(self, client: NexusClient):
        self._client = client

    def get_authentication_schemes_async(self) -> Awaitable[List[AuthenticationSchemeDescription]]:
        """Returns a list of available authentication schemes."""
        
        url: str = "/api/v1/users/authentication-schemes"

        return self._client.invoke_async(type(List[AuthenticationSchemeDescription]), "GET", url, "application/json", None)

    def authenticate_async(self, scheme: str, return_url: str) -> Awaitable[StreamResponse]:
        """Authenticates the user."""
        
        url: str = "/api/v1/users/authenticate"

        queryValues: Dict[str, str] = {
            "scheme": quote(str(scheme)),
            "return_url": quote(str(return_url)),
        }

        query: str = "?" + "&".join(f"{key}={value}" for (key, value) in queryValues.items())
        url += query

        return self._client.invoke_async(type(StreamResponse), "GET", url, "application/octet-stream", None)

    def sign_out_async(self, return_url: str) -> Awaitable[StreamResponse]:
        """Logs out the user."""
        
        url: str = "/api/v1/users/signout"

        queryValues: Dict[str, str] = {
            "return_url": quote(str(return_url)),
        }

        query: str = "?" + "&".join(f"{key}={value}" for (key, value) in queryValues.items())
        url += query

        return self._client.invoke_async(type(StreamResponse), "GET", url, "application/octet-stream", None)

    def refresh_token_async(self, request: RefreshTokenRequest) -> Awaitable[TokenPair]:
        """Refreshes the JWT token."""
        
        url: str = "/api/v1/users/refresh-token"

        return self._client.invoke_async(type(TokenPair), "POST", url, "application/json", request)

    def revoke_token_async(self, request: RevokeTokenRequest) -> Awaitable[StreamResponse]:
        """Revokes a refresh token."""
        
        url: str = "/api/v1/users/revoke-token"

        return self._client.invoke_async(type(StreamResponse), "POST", url, "application/octet-stream", request)

    def get_me_async(self) -> Awaitable[NexusUser]:
        """Gets the current user."""
        
        url: str = "/api/v1/users/me"

        return self._client.invoke_async(type(NexusUser), "GET", url, "application/json", None)

    def generate_tokens_async(self) -> Awaitable[TokenPair]:
        """Generates a set of tokens."""
        
        url: str = "/api/v1/users/generate-tokens"

        return self._client.invoke_async(type(TokenPair), "POST", url, "application/json", None)

    def get_users_async(self) -> Awaitable[List[NexusUser]]:
        """Gets a list of users."""
        
        url: str = "/api/v1/users"

        return self._client.invoke_async(type(List[NexusUser]), "GET", url, "application/json", None)

    def put_claim_async(self, user_id: str, claim_id: uuid, claim: NexusClaim) -> Awaitable[StreamResponse]:
        """Puts a claim."""
        
        url: str = "/api/v1/users/{userId}/{claimId}"
        url = url.replace("{user_id}", quote(str(user_id)))
        url = url.replace("{claim_id}", quote(str(claim_id)))

        return self._client.invoke_async(type(StreamResponse), "PUT", url, "application/octet-stream", claim)

    def delete_claim_async(self, user_id: str, claim_id: uuid) -> Awaitable[StreamResponse]:
        """Deletes a claim."""
        
        url: str = "/api/v1/users/{userId}/{claimId}"
        url = url.replace("{user_id}", quote(str(user_id)))
        url = url.replace("{claim_id}", quote(str(claim_id)))

        return self._client.invoke_async(type(StreamResponse), "DELETE", url, "application/octet-stream", None)


class WritersClient:
    """Provides methods to interact with writers."""

    _client: NexusClient
    
    def __init__(self, client: NexusClient):
        self._client = client

    def get_writer_descriptions_async(self) -> Awaitable[List[ExtensionDescription]]:
        """Gets the list of writers."""
        
        url: str = "/api/v1/writers/descriptions"

        return self._client.invoke_async(type(List[ExtensionDescription]), "GET", url, "application/json", None)




class NexusClient:
    """A client for the Nexus system."""
    
    _nexus_configuration_header_key: str = "Nexus-Configuration"
    _authorization_header_key: str = "Authorization"

    _token_folder_path: str = os.path.join(tempfile.gettempdir(), "nexus", "tokens")

    _token_pair: TokenPair
    _http_client: AsyncClient
    _token_file_path: str

    _artifacts: ArtifactsClient
    _catalogs: CatalogsClient
    _data: DataClient
    _jobs: JobsClient
    _packageReferences: PackageReferencesClient
    _sources: SourcesClient
    _users: UsersClient
    _writers: WritersClient


    # /// <param name="baseUrl">The base URL to connect to.</param>
    # public NexusClient(Uri baseUrl) : this(new HttpClient() { BaseAddress = baseUrl })
    # {
    #     //
    # }

    def __init__(self, http_client: AsyncClient):
        """
        Initializes a new instance of the NexusClient
        
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

