import array
import base64
import enum
import json
import struct
import sys
import time
from abc import ABC, abstractmethod
from datetime import datetime
from typing import Awaitable, Dict, List, Tuple
from urllib.parse import ParseResult, urlparse
from uuid import UUID, uuid3

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

    Id: str
    DataType: NexusDataType

    def __init__(self, id: str, dataType: NexusDataType):
        self.Id = id
        self.DataType = dataType

class Resource:

    Id: UUID
    Name: str
    Group: str
    Unit: str
    Description: str
    Metadata: Dict[str, str]
    Representations: List[Representation]

    def __init__(self, id: UUID, name: str, group: str, unit: str, metadata: Dict[str, str], representations: List[Representation]):
        self.Id = id
        self.Name = name
        self.Group = group
        self.Unit = unit
        self.Metadata = metadata
        self.Representations = representations

class Catalog:
    
    Id: str
    Metadata: Dict[str, str]
    Resources: List[Resource]

    def __init__(self, id: str, metadata: Dict[str, str], resources: List[Resource]):
        self.Id = id
        self.Metadata = metadata
        self.Resources = resources