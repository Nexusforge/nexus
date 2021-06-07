import array
import base64
import enum
import json
import struct
import sys
import time
from abc import ABC, abstractmethod
from datetime import datetime
from io import TextIOWrapper
from typing import Awaitable, Dict, List, Tuple
from urllib.parse import ParseResult, urlparse
from uuid import UUID, uuid3

from PythonRpcDataModel import Catalog, Dataset


class LogLevel(enum.Enum):
	Trace = 0
	Debug = 1
	Information = 2
	Warning = 3
	Error = 4
	Critical = 5

class Logger():

	_stream: TextIOWrapper

	def __init__(self, stream: TextIOWrapper):
		self._stream = stream

	def Log(self, log_level: LogLevel, message: str):

		message = {
			"LogLevel": log_level.name,
			"Message": message
		}

		self._stream.write(json.dumps(message))
		self._stream.write("\n")
		self._stream.flush()

class IDataSource(ABC):

	resource_locator: ParseResult
	parameters: Dict[str, str]
	logger: Logger

	async def on_parameters_set_async(self):
		pass

	@abstractmethod
	async def get_catalogs_async(self) -> Awaitable[List[Catalog]]:
		pass

	@abstractmethod
	async def get_time_range_async(self, catalogId: str) -> Awaitable[Tuple[datetime, datetime]]:
		pass

	@abstractmethod
	async def get_availability_async(self, catalogId: str, begin: datetime, end: datetime) -> Awaitable[float]:
		pass

	@abstractmethod
	async def read_single_async(self, dataset: Dataset, length: int, begin: datetime, end: datetime) -> Awaitable[Tuple[List[float], bytes]]:
		pass

	def dispose(self):
		pass

class RpcCommunicator:

	# It is very important to read the correct number of bytes.
	# simple buffer.read() will return data but subsequent buffer.write
	# will fail with error 22.

	_dataSource: IDataSource
	_isConnected: bool

	def __init__(self, dataSource: IDataSource):
		self._dataSource = dataSource
		self._isConnected = False

	async def run(self):

		while (True):

			# sys.stdin.buffer.read returns requested bytes or zero bytes when reaching EOF:
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
			if not self._isConnected:

				if ("protocol" in request and "version" in request):

					if (request["protocol"] == "json" and \
					    request["version"] == 1):

						response = {}
						self._isConnected = True

					else:
						response = {
							"error": "Only protocol 'json' of version 1 is supported.",
						}
					
				else:
					raise Exception(f"Handshake message expected, but got something else.")

			elif "type" in request:

				if request["type"] == 1:

					if "target" in request and \
					   "arguments" in request:

						response = await self._processInvocationAsync(request)

					else:
						raise Exception(f"Invalid invocation message received.")

				elif request["type"] == 7:
					self._dataSource.dispose()
					exit()

				else:
					raise Exception(f"Protocol message type '{request['type']}' is not supported.")

			else:
				raise Exception(f"Protocol message expected, but something else.") 
			
			# send response length and response message
			if response is not None:
				responseString = json.dumps(response, default=lambda x: self._serializeJson(x))
				responseBytes = bytes(responseString, "utf-8")
				responseLengthBytes = int.to_bytes(len(responseBytes), 4, "little")

				sys.stdout.buffer.write(responseLengthBytes)
				sys.stdout.buffer.write(responseBytes)
				sys.stdout.flush()

	async def _processInvocationAsync(self, request: any):

		if request["target"] == "SetParameters":

			resourceLocator = urlparse(request["arguments"][0])
			parameters = request["arguments"][1]
			logger = Logger(sys.stderr)

			self._dataSource.resource_locator = resourceLocator
			self._dataSource.parameters = parameters
			self._dataSource.logger = logger

			await self._dataSource.on_parameters_set_async()

		elif request["target"] == "GetCatalogs":

			catalogs = await self._dataSource.get_catalogs_async()

			return {
				"invocationId": request["invocationId"],
				"result": {
					"Catalogs": catalogs
				}
			}

		elif request["target"] == "GetTimeRange":

			catalogId = request["arguments"][0]
			(begin, end) = await self._dataSource.get_time_range_async(catalogId)

			return{
				"invocationId": request["invocationId"],
				"result": {
					"Begin": begin,
					"End": end,
				}
			}

		elif request["target"] == "GetAvailability":

			catalogId = request["arguments"][0]
			begin = datetime.strptime(request["arguments"][1], "%Y-%m-%dT%H:%M:%SZ")
			end = datetime.strptime(request["arguments"][2], "%Y-%m-%dT%H:%M:%SZ")
			availability = await self._dataSource.get_availability_async(catalogId, begin, end)

			return {
				"invocationId": request["invocationId"],
				"result": {
					"Availability": availability
				}
			}

		elif request["target"] == "ReadSingle":

			jsonDataset = request["arguments"][0]
			dataset = Dataset(jsonDataset["Id"])
			dataset.DataType = jsonDataset["DataType"]

			raise Exception(dataset.DataType)

			length = request["Length"]
			begin = datetime.strptime(request["arguments"][1], "%Y-%m-%dT%H:%M:%SZ")
			end = datetime.strptime(request["arguments"][2], "%Y-%m-%dT%H:%M:%SZ")
			(data, status) = await self._dataSource.read_single_async(dataset, length, begin, end)

			return {
				"invocationId": request["invocationId"],
				"result": {}
			}

	def _validateRequest(self, readCount: int):
		if readCount == 0:
			raise Exception("The connection aborted unexpectedly.")

	def _serializeJson(self, x):

		if isinstance(x, enum.Enum):
			return x._name_

		if isinstance(x, datetime):
			return x.isoformat()

		else:
			return x.__dict__
