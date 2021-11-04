# https://stackoverflow.com/questions/33533148/how-do-i-type-hint-a-method-with-the-type-of-the-enclosing-class
from __future__ import annotations

import enum
from datetime import timedelta
from typing import Dict, List, Tuple

# TODO: Make dict and list readonly, e.g. by using tuple instead of list 
# or adapt this solution: https://stackoverflow.com/questions/19022868/how-to-make-dictionary-read-only-in-python

class NexusDataType(enum.Enum):
    BOOLEAN = 0x008
    UINT8 = 0x108
    INT8 = 0x208
    UINT16 = 0x110
    INT16 = 0x210
    UINT32 = 0x120
    INT32 = 0x220
    UINT64 = 0x140
    INT64 = 0x240
    FLOAT32 = 0x320
    FLOAT64 = 0x340

class Representation:

    _dataType: NexusDataType
    _samplePeriod: timedelta
    _detail: str

    _postFixes = ["us", "ms", "s", "min"]
    _quotients = [1000, 1000, 60, 1 ]

    def __init__(self, dataType: NexusDataType, samplePeriod: timedelta, detail: str = None):

        maxPeriod = timedelta(seconds=86400)
        
        if samplePeriod >= maxPeriod:
            raise Exception("The sample period of the representation is too large.")

        self._dataType = dataType
        self._samplePeriod = samplePeriod
        self._detail = detail

    @property
    def Id(self) -> str:

        unitString = self._getUnitString(self._samplePeriod)
        return f"{unitString}" if self._detail is None or self._detail.isspace() else f"{unitString}_{self._detail}"

    @property
    def DataType(self) -> NexusDataType:
        return self._dataType

    @property
    def DataType(self) -> NexusDataType:
        return self._dataType

    @property
    def SamplePeriod(self) -> timedelta:
        return self._samplePeriod

    @property
    def Detail(self) -> str:
        return self._detail

    def _getUnitString(self, samplePeriod):
        
        currentValue = samplePeriod.total_seconds() * 1e6

        for i in range(len(self._postFixes)):

            quotient, remainder = divmod(currentValue, self._quotients[i])

            if remainder != 0:
                return f"{str(int(currentValue))}_{self._postFixes[i]}"

            else:
                currentValue = quotient

        return f"{str(int(currentValue))}_{self._postFixes[-1]}"

class Resource:

    _id: str
    _properties: Dict[str, str]
    _representations: List[Representation]

    def __init__(self, id: str, properties: Dict[str, str], representations: List[Representation]):
        self._id = id
        self._properties = properties
        self._representations = representations

    @property
    def Id(self) -> str:
        return self._id

    @property
    def Properties(self) -> Dict[str, str]:
        return self._properties

    @property
    def Representations(self) -> List[Representation]:
        return self._representations

class ResourceCatalog:
    
    _id: str
    _properties: Dict[str, str]
    _resources: List[Resource]

    def __init__(self, id: str, properties: Dict[str, str], resources: List[Resource]):
        self._id = id
        self._properties = properties
        self._resources = resources

    @property
    def Id(self) -> str:
        return self._id

    @property
    def Properties(self) -> Dict[str, str]:
        return self._properties

    @property
    def Resources(self) -> List[Resource]:
        return self._resources

class ResourceCatalogBuilder:

    _id: str
    _properties: Dict[str, str]
    _resources: List[Resource]

    def __init__(self, id: str):
        self._id = id
        self._properties = None
        self._resources = None

    def WithProperty(self, key: str, value: str) -> ResourceCatalogBuilder:

        if self._properties is None:
            self._properties = {}

        self._properties[key] = value

        return self

    def WithDescription(self, description: str) -> ResourceCatalogBuilder:
        return self.WithProperty("Description", description)

    def AddResource(self, resource: Resource) -> ResourceCatalogBuilder:

        if self._resources is None:
            self._resources = []

        self._resources.append(resource)

        return self

    def AddResources(self, resources: List[Resource]) -> ResourceCatalogBuilder:

        if self._resources is None:
            self._resources = []

        for resource in resources:
            self._resources.append(resource)

        return self

    def Build(self) -> ResourceCatalog:
        return ResourceCatalog(self._id, self._properties, self._resources)

class ResourceBuilder:

    _id: str
    _properties: Dict[str, str]
    _representations: List[Representation]

    def __init__(self, id: str):
        self._id = id
        self._properties = None
        self._representations = None

    def WithProperty(self, key: str, value: str) -> ResourceBuilder:

        if self._properties is None:
            self._properties = {}

        self._properties[key] = value

        return self

    def WithUnit(self, unit: str) -> ResourceBuilder:
        return self.WithProperty("Unit", unit)

    def WithDescription(self, description: str) -> ResourceBuilder:
        return self.WithProperty("Description", description)

    def WithWarning(self, warning: str) -> ResourceBuilder:
        return self.WithProperty("Warning", warning)

    def WithGroups(self, groups: List[str]) -> ResourceBuilder:

        counter = 0

        for group in groups:
            self.WithProperty(f"Groups:{counter}", group)
            counter += 1

        return self

    def AddRepresentation(self, representation: Representation) -> ResourceBuilder:

        if self._representations is None:
            self._representations = []

        self._representations.append(representation)

        return self

    def AddRepresentations(self, representations: List[Representation]) -> ResourceBuilder:

        if self._representations is None:
            self._representations = []

        for representation in representations:
            self._representations.append(representation)

        return self

    def Build(self) -> Resource:
        return Resource(self._id, self._properties, self._representations)
