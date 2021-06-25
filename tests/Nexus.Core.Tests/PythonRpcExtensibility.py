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

class DataSourceContext:

    resource_locator: ParseResult
    configuration: Dict[str, str]

    catalogs: List[Catalog]

    def __init__(self, resource_locator: ParseResult, configuration: Dict[str, str], logger: Logger, catalogs: List[Catalog]):
        self.resource_locator = resource_locator
        self.configuration = configuration
        self.logger = logger
        self.catalogs = catalogs

class IDataSource(ABC):

    async def set_context_async(self):
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
    async def read_single_async(self, datasetPath: str, length: int, begin: datetime, end: datetime) -> Awaitable[Tuple[List[float], bytes]]:
        pass

    def dispose(self):
        pass

class RpcCommunicator:

    _dataSource: IDataSource
    _isConnected: bool

    def __init__(self, dataSource: IDataSource):
        self._dataSource = dataSource
        self._isConnected = False

    async def run(self):

        while (True):

            # sys.stdin.buffer.read returns requested bytes or zero bytes when reaching EOF:
            # (https://docs.python.org/3/library/io.html#io.BufferedIOBase.read)

            # get request message
            jsonRequest = sys.stdin.readline()
            self._validateRequest(len(jsonRequest))
            request = json.loads(jsonRequest)
            
            # process message
            data = None
            status = None

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

                        (response, data, status) = await self._processInvocationAsync(request)

                    else:
                        raise Exception(f"Invalid invocation message received.")

                elif request["type"] == 7:
                    self._dataSource.dispose()
                    exit()

                else:
                    raise Exception(f"Protocol message type '{request['type']}' is not supported.")

            else:
                raise Exception(f"Protocol message expected, but something else.") 
            
            # send response
            if response is not None:
                jsonResponse = json.dumps(response, default=lambda x: self._serializeJson(x))
                sys.stdout.write(jsonResponse)
                sys.stdout.write("\n")
                sys.stdout.flush()

            if data is not None and status is not None:
                sys.stdout.buffer.write(data)
                sys.stdout.buffer.write(status)
                sys.stdout.flush()

    async def _processInvocationAsync(self, request: any):
        
        response = None
        data = None
        status = None

        if request["target"] == "GetApiLevel":

            response = {
                "invocationId": request["invocationId"],
                "result": {
                    "ApiLevel": 1
                }
            }

        elif request["target"] == "SetContext":

            resource_locator = urlparse(request["arguments"][0])
            configuration = request["arguments"][1]
            logger = Logger(sys.stderr)
            catalogs = request["arguments"][2]
            context = DataSourceContext(resource_locator, configuration, logger, catalogs)

            await self._dataSource.set_context_async(context)

        elif request["target"] == "GetCatalogs":

            catalogs = await self._dataSource.get_catalogs_async()

            response = {
                "invocationId": request["invocationId"],
                "result": {
                    "Catalogs": catalogs
                }
            }

        elif request["target"] == "GetTimeRange":

            catalogId = request["arguments"][0]
            (begin, end) = await self._dataSource.get_time_range_async(catalogId)

            response = {
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

            response = {
                "invocationId": request["invocationId"],
                "result": {
                    "Availability": availability
                }
            }

        elif request["target"] == "ReadSingle":

            datasetPath = request["arguments"][0]
            length = request["arguments"][1]
            begin = datetime.strptime(request["arguments"][2], "%Y-%m-%dT%H:%M:%SZ")
            end = datetime.strptime(request["arguments"][3], "%Y-%m-%dT%H:%M:%SZ")
            (data, status) = await self._dataSource.read_single_async(datasetPath, length, begin, end)

            response =  ({
                "invocationId": request["invocationId"],
                "result": {}
            })

        return (response, data, status)

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
