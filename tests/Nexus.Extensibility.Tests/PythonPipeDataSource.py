import array
import base64
import enum
import json
import struct
import sys
import time
from uuid import UUID, uuid3
from abc import ABC, abstractmethod
from typing import List


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
	def get_catalogs_async(self) -> List[Catalog]:
		pass

def FromBase64Bytes(base64Bytes) -> int:
	normalBytes = base64.b64decode(base64Bytes)
	return int.from_bytes(normalBytes, 'little')

def ToBase64Bytes(length) -> bytes:
	normalBytes = struct.pack('<I', length)
	return base64.b64encode(normalBytes)

def ValidateRequest(readCount, expectedCount):

	if readCount == 0:
		raise Exception("The connection aborted unexpectedly.")

	elif readCount != expectedCount:
		raise Exception("Invalid number of bytes received.")

class PythonDataSource(IDataSource):
	
	def get_catalogs_async(self) -> List[Catalog]:

		class NULL_NAMESPACE:
			bytes = b''

		# catalog 1
		catalog1_channel1_dataset1 = Dataset("1 Hz_mean")
		catalog1_channel1_dataset1.DataType = NexusDataType.FLOAT32

		catalog1_channel1 = Channel(str(uuid3(NULL_NAMESPACE, "catalog1_channel1")))
		catalog1_channel1.Name = "channel1"
		catalog1_channel1.Group = "group1"
		catalog1_channel1.Description = "description1"
		catalog1_channel1.Unit = "°C"
		catalog1_channel1.Datasets.append(catalog1_channel1_dataset1)

		catalog1_channel2_dataset1 = Dataset("1 Hz_mean")
		catalog1_channel2_dataset1.DataType = NexusDataType.FLOAT64

		catalog1_channel2 = Channel(str(uuid3(NULL_NAMESPACE, "catalog1_channel2")))
		catalog1_channel2.Name = "channel2"
		catalog1_channel2.Group = "group2"
		catalog1_channel2.Description = "description2"
		catalog1_channel2.Unit = "bar"
		catalog1_channel2.Datasets.append(catalog1_channel2_dataset1)

		catalog1 = Catalog("/A/B/C")
		catalog1.Channels.append(catalog1_channel1)
		catalog1.Channels.append(catalog1_channel2)

		# catalog 2
		catalog2_channel1_dataset1 = Dataset("1 Hz_mean")
		catalog2_channel1_dataset1.DataType = NexusDataType.INT64

		catalog2_channel1 = Channel(str(uuid3(NULL_NAMESPACE, "catalog2_channel1")))
		catalog2_channel1.Name = "channel1"
		catalog2_channel1.Group = "group1"
		catalog2_channel1.Description = "description1"
		catalog2_channel1.Unit = "m/s"
		catalog2_channel1.Datasets.append(catalog2_channel1_dataset1)

		catalog2 = Catalog("/D/E/F")
		catalog2.Channels.append(catalog2_channel1)

		# return
		return [catalog1, catalog2]

def SerializeJson(x):

	if isinstance(x, enum.Enum):
		return x._name_

	else:
		return x.__dict__
	#äraise Exception(x.Channels[0].Datasets[0].DataType.__dict__)

try:
	dataSource = PythonDataSource()

	# It is very important to read the correct number of bytes.
	# simple buffer.read() will return data but subsequent buffer.write
	# will fail with error 22.

	# get request length
	requestLengthBase64 = sys.stdin.buffer.read(8)
	ValidateRequest(len(requestLengthBase64), 8)
	requestLength = FromBase64Bytes(requestLengthBase64)

	# get request message
	requestBytes = sys.stdin.buffer.read(requestLength)
	ValidateRequest(len(requestBytes), requestLength)
	request = json.loads(requestBytes)
	
	# process message
	if (request["Type"] == "CatalogsRequest" and \
		request["Version"] == 1):

		catalogs = dataSource.get_catalogs_async()

		response = {
			"Type": "CatalogsResponse",
			"Version": 1,
			"Catalogs": catalogs
		}

	else:
		raise Exception("Not supported.")

	# send response length and response message
	responseString = json.dumps(response, default=lambda x: SerializeJson(x))
	responseBytes = bytes(responseString, "utf-8")
	responseLengthBase64 = ToBase64Bytes(len(responseBytes))
	
	sys.stdout.buffer.write(responseLengthBase64)
	sys.stdout.buffer.write(responseBytes)
	sys.stdout.flush()

	time.sleep(10)

except Exception as exception:
	with open("D:/Git/Test/namedpipes/namedpipeserver/error.txt", "w") as text_file:
		import traceback
		print(traceback.format_exc(), file=text_file)
