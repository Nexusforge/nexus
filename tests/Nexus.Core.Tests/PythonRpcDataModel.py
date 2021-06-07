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

	def __init__(self, id: str):
		self.Id = id

class Channel:

	Id: UUID
	Name: str
	Group: str
	Unit: str
	Description: str
	Datasets: List[Dataset]

	def __init__(self, id: UUID):
		self.Id = id
		self.Datasets = []

class Catalog:
	
	Id: str
	Channels: List[Channel]

	def __init__(self, id: str):
		self.Id = id
		self.Channels = []