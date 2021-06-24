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

class Dataset:

    Id: str
    DataType: NexusDataType

    def __init__(self, id: str, dataType: NexusDataType):
        self.Id = id
        self.DataType = dataType

class Channel:

    Id: UUID
    Name: str
    Group: str
    Unit: str
    Description: str
    Metadata: Dict[str, str]
    Datasets: List[Dataset]

    def __init__(self, id: UUID, name: str, group: str, unit: str, metadata: Dict[str, str], datasets: List[Dataset]):
        self.Id = id
        self.Name = name
        self.Group = group
        self.Unit = unit
        self.Metadata = metadata
        self.Datasets = datasets

class Catalog:
    
    Id: str
    Metadata: Dict[str, str]
    Channels: List[Channel]

    def __init__(self, id: str, metadata: Dict[str, str], channels: List[Channel]):
        self.Id = id
        self.Metadata = metadata
        self.Channels = channels