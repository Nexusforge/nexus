import array
import base64
import enum
import json
import struct
import sys
import time
from abc import ABC, abstractmethod
from datetime import datetime
from typing import Awaitable, List, Tuple
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

class IDataSource(ABC):

	@abstractmethod
	async def get_catalogs_async(self) -> Awaitable[List[Catalog]]:
		pass

	@abstractmethod
	async def get_time_range_async(self) -> Awaitable[Tuple[datetime, datetime]]:
		pass

	@abstractmethod
	async def get_availability_async(self, begin: datetime, end: datetime) -> Awaitable[float]:
		pass

	@abstractmethod
	async def read_single_async(self, dataset: Dataset, length: int, begin: datetime, end: datetime) -> Awaitable[float]:
		pass

	def dispose(self):
		pass

class PipeCommunicator:

	# It is very important to read the correct number of bytes.
	# simple buffer.read() will return data but subsequent buffer.write
	# will fail with error 22.

	_dataSource: IDataSource

	def __init__(self, dataSource: IDataSource):
		self._dataSource = dataSource

	async def run(self):

		while (True):
			# sys.stdin.buffer.read returns requests bytes or zero bytes when reaching EOF:
			# (https://docs.python.org/3/library/io.html#io.BufferedIOBase.read)

			# get request length
			requestLengthBytes = sys.stdin.buffer.read(4)
			self._validateRequest(len(requestLengthBytes))
			requestLength = int.from_bytes(requestLengthBytes, 'little')

			# get request message
			requestBytes = sys.stdin.buffer.read(requestLength)
			self._validateRequest(len(requestBytes))
			request = json.loads(requestBytes)
			
			# process message
			if (request["Type"] == "ProtocolRequest"):

				if ("nexus_pipes_v1" in request["AvailableProtocols"]):
					response = {
						"Type": "ProtocolResponse",
						"SelectedProtocol": "nexus_pipes_v1"
					}
					
				else:
					response = {
						"Type": "ProtocolResponse",
						"SelectedProtocol": None
					}

			elif (request["Type"] == "CatalogsRequest" and \
				  request["Version"] == 1):

				catalogs = await self._dataSource.get_catalogs_async()

				response = {
					"Type": "CatalogsResponse",
					"Version": 1,
					"Catalogs": catalogs
				}

			elif (request["Type"] == "TimeRangeRequest" and \
				  request["Version"] == 1):

				(begin, end) = await self._dataSource.get_time_range_async()

				response = {
					"Type": "TimeRangeResponse",
					"Version": 1,
					"Begin": begin,
					"End": end,
				}

			elif (request["Type"] == "AvailabilityRequest" and \
				  request["Version"] == 1):

				begin = datetime.strptime(request["Begin"], "%Y-%m-%dT%H:%M:%SZ")
				end = datetime.strptime(request["End"], "%Y-%m-%dT%H:%M:%SZ")
				availability = await self._dataSource.get_availability_async(begin, end)

				response = {
					"Type": "AvailabilityResponse",
					"Version": 1,
					"Availability": availability
				}

			elif (request["Type"] == "ReadSingleRequest" and \
				  request["Version"] == 1):

				dataset = #needs to be casted
				length = request["Length"]
				begin = datetime.strptime(request["Begin"], "%Y-%m-%dT%H:%M:%SZ")
				end = datetime.strptime(request["End"], "%Y-%m-%dT%H:%M:%SZ")
				availability = await self._dataSource.read_single_async(dataset, length, begin, end)

				response = {
					"Type": "AvailabilityResponse",
					"Version": 1,
					"Availability": availability
				}

			elif (request["Type"] == "ShutdownRequest" and \
				  request["Version"] == 1):

				self._dataSource.dispose()
				exit()

			else:
				raise Exception(f"The protocol message of type '{request['Type']}' and version '{request['Version']}' is not supported.")
			
			# send response length and response message
			responseString = json.dumps(response, default=lambda x: self._serializeJson(x))
			responseBytes = bytes(responseString, "utf-8")
			responseLengthBytes = int.to_bytes(len(responseBytes), 4, "little")

			sys.stdout.buffer.write(responseLengthBytes)
			sys.stdout.buffer.write(responseBytes)
			sys.stdout.flush()

	def _validateRequest(self, readCount):
		if readCount == 0:
			raise Exception("The connection aborted unexpectedly.")

	def _serializeJson(self, x):

		if isinstance(x, enum.Enum):
			return x._name_

		if isinstance(x, datetime):
			return x.isoformat()

		else:
			return x.__dict__
