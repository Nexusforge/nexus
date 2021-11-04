import enum
import json
import socket
import struct
import time
from abc import ABC, abstractmethod
from datetime import datetime, timedelta
from io import TextIOWrapper
from threading import Lock
from typing import Awaitable, Dict, List, Tuple
from urllib.parse import ParseResult, urlparse

from PythonRpcDataModel import ResourceCatalog


class LogLevel(enum.Enum):
    Trace = 0
    Debug = 1
    Information = 2
    Warning = 3
    Error = 4
    Critical = 5

class Logger():

    _tcpCommSocket: socket
    _lock: Lock

    def __init__(self, tcpSocket: socket, lock: Lock):
        self._tcpCommSocket = tcpSocket
        self._lock = lock

    def log(self, log_level: LogLevel, message: str):

        notification = {
            "jsonrpc": "2.0",
            "method": "log",
            "params": [log_level.name, message]
        }

        jsonResponse = json.dumps(notification, default=lambda x: self._serializeJson(x), ensure_ascii = False)
        encodedResponse = jsonResponse.encode()

        with self._lock:
            self._tcpCommSocket.sendall(struct.pack(">I", len(encodedResponse)))
            self._tcpCommSocket.sendall(encodedResponse)

class DataSourceContext:

    resource_locator: ParseResult
    configuration: Dict[str, str]
    logger: Logger

    def __init__(self, resource_locator: ParseResult, configuration: Dict[str, str], logger: Logger):
        self.resource_locator = resource_locator
        self.configuration = configuration
        self.logger = logger

class IDataSource(ABC):

    async def set_context_async(self):
        pass

    @abstractmethod
    async def get_catalog_ids_async(self) -> Awaitable[List[str]]:
        pass

    @abstractmethod
    async def get_catalog_async(self, catalogId: str) -> Awaitable[ResourceCatalog]:
        pass

    @abstractmethod
    async def get_time_range_async(self, catalogId: str) -> Awaitable[Tuple[datetime, datetime]]:
        pass

    @abstractmethod
    async def get_availability_async(self, catalogId: str, begin: datetime, end: datetime) -> Awaitable[float]:
        pass

    @abstractmethod
    async def read_single_async(self, resourcePath: str, length: int, begin: datetime, end: datetime) -> Awaitable[Tuple[List[float], bytes]]:
        pass

    def dispose(self):
        pass

class RpcCommunicator:

    _address: str
    _port: int
    _dataSource: IDataSource
    _lock: Lock
    _tcpCommSocket: socket
    _tcpDataSocket: socket

    def __init__(self, dataSource: IDataSource, address: str, port: int):

        self._address = address
        self._port = port
        self._lock = Lock()
        self._tcpCommSocket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._tcpDataSocket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)

        if (not (0 < port and port < 65536)):
            raise Exception(f"The port {port} is not a valid port number.")

        self._dataSource = dataSource

    async def run(self):

        # comm connection
        self._tcpCommSocket.connect((self._address, self._port))
        self._tcpCommSocket.sendall("comm".encode())

        # data connection
        self._tcpDataSocket.connect((self._address, self._port))
        self._tcpDataSocket.sendall("data".encode())

        # loop
        while (True):

            # https://www.jsonrpc.org/specification

            # get request message
            sizeBuffer = self._tcpCommSocket.recv(4, socket.MSG_WAITALL)

            if len(sizeBuffer) == 0:
                self._shutdown()

            size = struct.unpack(">I", sizeBuffer)[0]

            jsonRequest = self._tcpCommSocket.recv(size, socket.MSG_WAITALL)

            if len(sizeBuffer) == 0:
                self._shutdown()

            request = json.loads(jsonRequest)

            # process message
            data = None
            status = None

            if "jsonrpc" in request and request["jsonrpc"] == "2.0":

                if "id" in request:

                    try:
                        (result, data, status) = await self._processInvocationAsync(request)

                        response = {
                            "result": result
                        }

                    except Exception as ex:
                        
                        response = {
                            "error": {
                                "code": -1,
                                "message": str(ex)
                            }
                        }

                else:
                    raise Exception(f"JSON-RPC 2.0 notifications are not supported.") 

            else:              
                raise Exception(f"JSON-RPC 2.0 message expected, but got something else.") 
            
            response["jsonrpc"] = "2.0"
            response["id"] = request["id"]

            # send response
            jsonResponse = json.dumps(response, default=lambda x: self._serializeJson(x), ensure_ascii = False)
            encodedResponse = jsonResponse.encode()

            with self._lock:
                self._tcpCommSocket.sendall(struct.pack(">I", len(encodedResponse)))
                self._tcpCommSocket.sendall(encodedResponse)

            # send data
            if data is not None and status is not None:
                self._tcpDataSocket.sendall(data)
                self._tcpDataSocket.sendall(status)

    async def _processInvocationAsync(self, request: any):
        
        result = None
        data = None
        status = None

        methodName = request["method"]
        params = request["params"]

        if methodName == "getApiVersionAsync":

            result = {
                "ApiVersion": 1
            }

        elif methodName == "setContextAsync":

            resource_locator = urlparse(params[0])
            configuration = params[1]
            logger = Logger(self._tcpCommSocket, self._lock)
            context = DataSourceContext(resource_locator, configuration, logger)

            await self._dataSource.set_context_async(context)

        elif methodName == "getCatalogIdsAsync":

            catalogIds = await self._dataSource.get_catalog_ids_async()

            result = {
                "CatalogIds": catalogIds
            }

        elif methodName == "getCatalogAsync":

            catalogId = params[0]
            catalog = await self._dataSource.get_catalog_async(catalogId)
            
            result = {
                "Catalog": catalog
            }

        elif methodName == "getTimeRangeAsync":

            catalogId = params[0]
            (begin, end) = await self._dataSource.get_time_range_async(catalogId)

            result = {
                "Begin": begin,
                "End": end,
            }

        elif methodName == "getAvailabilityAsync":

            catalogId = params[0]
            begin = datetime.strptime(params[1], "%Y-%m-%dT%H:%M:%SZ")
            end = datetime.strptime(params[2], "%Y-%m-%dT%H:%M:%SZ")
            availability = await self._dataSource.get_availability_async(catalogId, begin, end)

            result = {
                "Availability": availability
            }

        elif methodName == "readSingleAsync":

            resourcePath = params[0]
            length = params[1]
            begin = datetime.strptime(params[2], "%Y-%m-%dT%H:%M:%SZ")
            end = datetime.strptime(params[3], "%Y-%m-%dT%H:%M:%SZ")
            (data, status) = await self._dataSource.read_single_async(resourcePath, length, begin, end)

        # Add cancellation support?
        # https://github.com/microsoft/vs-streamjsonrpc/blob/main/doc/sendrequest.md#cancellation
        # https://github.com/Microsoft/language-server-protocol/blob/main/versions/protocol-2-x.md#cancelRequest
        elif methodName == "$/cancelRequest":
            pass

        else:
            raise Exception(f"Unknown method '{methodName}'.")

        return (result, data, status)

    def _shutdown(self, readCount: int):
        if readCount == 0:
            self._dataSource.dispose()
            exit()

    def _serializeJson(self, x):

        if isinstance(x, enum.Enum):
            return x._name_

        if isinstance(x, timedelta):
            return str(x)

        if isinstance(x, datetime):
            return x.isoformat()

        else:
            return {key.lstrip('_'): value for key, value in vars(x).items()}