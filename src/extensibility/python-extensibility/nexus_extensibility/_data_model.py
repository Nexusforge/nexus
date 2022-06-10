# https://stackoverflow.com/questions/33533148/how-do-i-type-hint-a-method-with-the-type-of-the-enclosing-class
from __future__ import annotations

import enum
import re
from dataclasses import dataclass
from datetime import timedelta
from typing import Any, List, Optional, Pattern, cast

from ._data_model_extensions import to_unit_string

_DESCRIPTION = "Description"
_WARNING = "Warning"
_UNIT = "Unit"
_GROUPS = "Groups"

# TODO: Make object and list readonly, e.g. by using tuple instead of list 
# or adapt this solution: https://stackoverflow.com/questions/19022868/how-to-make-dictionary-read-only-in-python

################# DATA MODEL TYPES ###############

class NexusDataType(enum.IntEnum):
    """Specifies the Nexus data type."""

    UINT8 = 0x108
    """Unsigned 8-bit integer."""

    INT8 = 0x208
    """Signed 8-bit integer."""

    UINT16 = 0x110
    """Unsigned 16-bit integer."""

    INT16 = 0x210
    """Signed 16-bit integer."""

    UINT32 = 0x120
    """Unsigned 32-bit integer."""

    INT32 = 0x220
    """Signed 32-bit integer."""

    UINT64 = 0x140
    """Unsigned 64-bit integer."""

    INT64 = 0x240
    """Signed 64-bit integer."""

    FLOAT32 = 0x320
    """32-bit floating-point number."""

    FLOAT64 = 0x340
    """64-bit floating-point number."""

@dataclass
class CatalogItem:
    """
    A catalog item consists of a catalog, a resource and a representation.

    Args:
        catalog: The catalog.
        resource: The resource.
        representation: The representation.
    """

    catalog: ResourceCatalog
    """The catalog."""

    resource: Resource
    """The resource."""

    representation: Representation
    """The representation."""

    def to_path(self) -> str:
        return f"{self.catalog.id}/{self.resource.id}/{self.representation.id}"

@dataclass
class CatalogRegistration:
    """
    A catalog registration.

    Args:
        path: The absolute or relative path of the catalog.
        title: The catalog title.
        is_transient: A boolean which indicates if the catalog and its children should be reloaded on each request.
    """

    path: str
    """The absolute or relative path of the catalog."""

    title: str
    """The catalog title."""

    is_transient: bool = False
    """A boolean which indicates if the catalog and its children should be reloaded on each request."""

################# DATA MODEL ###############

class Representation:
    """
    A representation is part of a resource.
    """

    _nexus_data_type_values: set[int] = set(item.value for item in NexusDataType) 

    def __init__(self, data_type: NexusDataType, sample_period: timedelta):
        """
        Initializes a new instance of a Representation
        
            Args:
                data_type: The data type.
                sample_period: The sample period.
        """

        # data type
        if not data_type in Representation._nexus_data_type_values:
            raise Exception(f"The identifier {data_type} is not valid.")

        self._data_type: NexusDataType = data_type

        # sample period
        if sample_period == timedelta(0):
            raise Exception(f"The sample period {sample_period} is not valid.")

        self._sample_period: timedelta = sample_period

        # id
        self._id: str = to_unit_string(sample_period)

    @property
    def data_type(self) -> NexusDataType:
        """The data type."""
        return self._data_type

    @property
    def sample_period(self) -> timedelta:
        """The sample period."""
        return self._sample_period

    @property
    def id(self) -> str:
        """The identifer of the representation. It is constructed using the sample period."""
        return self._id

    @property
    def element_size(self) -> int:
        """The number of bits per element."""
        return (int(self.data_type) and 0xFF) >> 3

class Resource:
    """
    A resource is part of a resource catalog and holds a list of representations.
    """

    _id_validator : Pattern[str] = re.compile(r"[a-zA-Z][a-zA-Z0-9_]*$")

    def __init__(self, id: str, properties: Optional[object] = None, representations: Optional[List[Representation]] = None):
        """
        Initializes a new instance of the Resource
        
            Args:
                id: The resource identifier.
                properties: The properties.
                representations: The list of representations.
        """

        if not self._id_validator.match(id):
            raise Exception(f"The resource catalog identifier {id} is not valid.")

        self._id: str = id
        self._properties: Optional[object] = properties

        if representations is not None:
            self._validate_representations(representations)

        self._representations: Optional[List[Representation]] = representations

    @property
    def id(self) -> str:
        """Gets the identifier."""
        return self._id

    @property
    def properties(self) -> Optional[object]:
        """Gets the properties."""
        return self._properties

    @property
    def representations(self) -> Optional[List[Representation]]:
        """Gets list of representations."""
        return self._representations

    def _validate_representations(self, representations: List[Representation]):
        unique_ids = set([representation.id for representation in representations])

        if len(unique_ids) != len(representations):
            raise Exception("There are multiple representations with the same identifier.")

class ResourceCatalog:
    """
    A catalog is a top level element and holds a list of resources.
    """

    _id_validator : Pattern[str] = re.compile(r"(?:\/[a-zA-Z][a-zA-Z0-9_]*)+$")

    def __init__(self, id: str, properties: Optional[object] = None, resources: Optional[List[Resource]] = None):
        """
        Initializes a new instance of the ResourceCatalog
        
            Args:
                id: The catalog identifier.
                properties: The properties.
                resource: The list of resources.
        """
        if not self._id_validator.match(id):
            raise Exception(f"The resource catalog identifier {id} is not valid.")

        self._id: str = id
        self._properties: Optional[object] = properties

        if resources is not None:
            self._validate_resources(resources)

        self._resources: Optional[List[Resource]] = resources

    @property
    def id(self) -> str:
        """Gets the identifier."""
        return self._id

    @property
    def properties(self) -> Optional[object]:
        """Gets the properties."""
        return self._properties

    @property
    def resources(self) -> Optional[List[Resource]]:
        """Gets the list of resources."""
        return self._resources

    def _validate_resources(self, resources: List[Resource]):
        unique_ids = set([resource.id for resource in resources])

        if len(unique_ids) != len(resources):
            raise Exception("There are multiple resource with the same identifier.")

class ResourceCatalogBuilder:
    """
    A catalog is a top level element and holds a list of resources.
    """

    def __init__(self, id: str):
        """
        Initializes a new instance of the ResourceCatalogBuilder
        
            Args:
                id: The identifier of the resource catalog to be built.
        """
        self._id: str = id
        self._properties: Optional[object] = None
        self._resources: Optional[List[Resource]] = None

    def with_property(self, key: str, value: Any) -> ResourceCatalogBuilder:
        """
        Adds a property.
        
            Args:
                key: The key of the property.
                value: The value of the property.
        """

        if self._properties is None:
            self._properties = {}

        cast(dict, self._properties)[key] = value

        return self

    def with_description(self, description: str) -> ResourceCatalogBuilder:
        """
        Adds a description.
        
            Args:
                description: The description to add.
        """
        return self.with_property(_DESCRIPTION, description)

    def add_resource(self, resource: Resource) -> ResourceCatalogBuilder:
        """
        Adds a resource.
        
            Args:
                resource: The resource.
        """

        if self._resources is None:
            self._resources = []

        self._resources.append(resource)

        return self

    def add_resources(self, resources: List[Resource]) -> ResourceCatalogBuilder:
        """
        Adds a list of resources.
        
            Args:
                resource: The list of resources.
        """

        if self._resources is None:
            self._resources = []

        for resource in resources:
            self._resources.append(resource)

        return self

    def build(self) -> ResourceCatalog:
        """
        Builds the resource catalog.
        """
        return ResourceCatalog(self._id, self._properties, self._resources)

class ResourceBuilder:
    """
    A resource builder simplifies building a resource.
    """

    def __init__(self, id: str):
        """
        Initializes a new instance of the ResourceBuilder
        
            Args:
                id: The identifier of the resource to be built.
        """
        self._id: str = id
        self._properties: Optional[object] = None
        self._representations: Optional[List[Representation]] = None

    def with_property(self, key: str, value: Any) -> ResourceBuilder:
        """
        Adds a property.
        
            Args:
                key: The key of the property.
                value: The value of the property.
        """

        if self._properties is None:
            self._properties = {}

        cast(dict, self._properties)[key] = value

        return self

    def with_unit(self, unit: str) -> ResourceBuilder:
        """
        Adds a unit.
        
            Args:
                unit: The unit to add.
        """
        return self.with_property(_UNIT, unit)

    def with_description(self, description: str) -> ResourceBuilder:
        """
        Adds a description.
        
            Args:
                description: The description to add.
        """
        return self.with_property(_DESCRIPTION, description)

    def with_warning(self, warning: str) -> ResourceBuilder:
        """
        Adds a warning.
        
            Args:
                warning: The warning to add.
        """
        return self.with_property(_WARNING, warning)

    def with_groups(self, groups: List[str]) -> ResourceBuilder:
        """
        Adds groups.
        
            Args:
                groups: The groups to add.
        """
        return self.with_property(_GROUPS, groups)

    def add_representation(self, representation: Representation) -> ResourceBuilder:
        """
        Adds a representation.
        
            Args:
                representation: The representation.
        """

        if self._representations is None:
            self._representations = []

        self._representations.append(representation)

        return self

    def add_representations(self, representations: List[Representation]) -> ResourceBuilder:
        """
        Adds a list of representations.
        
            Args:
                representations: The list of representations.
        """

        if self._representations is None:
            self._representations = []

        for representation in representations:
            self._representations.append(representation)

        return self

    def build(self) -> Resource:
        """
        Builds the resource.
        """

        return Resource(self._id, self._properties, self._representations)
