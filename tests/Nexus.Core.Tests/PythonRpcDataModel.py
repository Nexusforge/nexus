import array
import base64
import enum
import json
import struct
import sys
import time
from abc import ABC, abstractmethod
from datetime import datetime, timedelta
from typing import Awaitable, Dict, List, Tuple
from urllib.parse import ParseResult, urlparse


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

    SamplePeriod: timedelta
    Detail: str
    DataType: NexusDataType

    _postFixes = ["us", "ms", "s", "min"]
    _quotients = [1000, 1000, 60, 1 ]

    def __init__(self, samplePeriod: timedelta, detail: str, dataType: NexusDataType):

        maxPeriod = timedelta(seconds=86400)

        if samplePeriod >= maxPeriod:
            raise Exception("The sample period of the representation is too large.")

        self.SamplePeriod = samplePeriod
        self.Detail = detail
        self.DataType = dataType

    @property
    def Id(self):

        unitString = self._getUnitString(self.SamplePeriod)
        return f"{unitString}" if self.Detail is None or self.Detail.isspace() else f"{unitString}_{self.Detail}"

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

    Id: str
    Unit: str
    Groups: List[str]
    Metadata: Dict[str, str]
    Representations: List[Representation]

    def __init__(self, id: str, unit: str, groups: List[str], metadata: Dict[str, str], representations: List[Representation]):
        self.Id = id
        self.Groups = groups
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
