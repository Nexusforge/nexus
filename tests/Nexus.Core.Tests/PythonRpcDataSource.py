import asyncio
import datetime
import glob
import os
import sys
from array import array
from datetime import datetime, timedelta, timezone
from urllib.request import url2pathname

from PythonRpcDataModel import (NexusDataType, Representation, ResourceBuilder,
                                ResourceCatalogBuilder)
from PythonRpcExtensibility import IDataSource, LogLevel, RpcCommunicator


class NULL_NAMESPACE:
    bytes = b''

class PythonDataSource(IDataSource):
    
    _context = None

    async def set_context_async(self, context):
        
        self._context = context

        if (context.resource_locator.scheme != "file"):
            raise Exception(f"Expected 'file' URI scheme, but got '{self.resource_locator.scheme}'.")

        context.logger.log(LogLevel.Information, "Logging works!")

    async def get_catalogs_async(self):

        if (self._context.catalogs is None):

            # catalog 1
            representation = Representation(NexusDataType.INT64, timedelta(seconds=1), "mean")

            resource1 = ResourceBuilder("resource1") \
                .WithUnit("°C") \
                .WithGroups(["group1"]) \
                .AddRepresentation(representation) \
                .Build()

            representation = Representation(NexusDataType.FLOAT64, timedelta(seconds=1), "mean")

            resource2 = ResourceBuilder("resource2") \
                .WithUnit("bar") \
                .WithGroups(["group2"]) \
                .AddRepresentation(representation) \
                .Build()

            catalog1 = ResourceCatalogBuilder("/A/B/C") \
                .WithProperty("a", "b") \
                .AddResources([resource1, resource2]) \
                .Build()

            # catalog 2
            representation = Representation(NexusDataType.FLOAT32, timedelta(seconds=1), "mean")

            resource = ResourceBuilder("resource1") \
                .WithUnit("m/s") \
                .WithGroups(["group1"]) \
                .AddRepresentation(representation) \
                .Build()

            catalog2 = ResourceCatalogBuilder("/D/E/F") \
                .AddResource(resource) \
                .Build()

            #
            self._context.catalogs = [catalog1, catalog2]

        # return
        return self._context.catalogs

    async def get_time_range_async(self, catalogId: str):

        if catalogId != "/A/B/C":
            raise Exception("Unknown catalog ID.")

        filePaths = glob.glob(url2pathname(self._context.resource_locator.path) + "/**/*.dat", recursive=True)
        fileNames = [os.path.basename(filePath) for filePath in filePaths]
        dateTimes = sorted([datetime.strptime(fileName, '%Y-%m-%d_%H-%M-%S.dat') for fileName in fileNames])
        begin = dateTimes[0].replace(tzinfo = timezone.utc)
        end = dateTimes[-1].replace(tzinfo = timezone.utc)

        return (begin, end)

    async def get_availability_async(self, catalogId: str, begin: datetime, end: datetime):

        if catalogId != "/A/B/C":
            raise Exception("Unknown catalog ID.")

        periodPerFile = timedelta(minutes = 10)
        maxFileCount = (end - begin).total_seconds() / periodPerFile.total_seconds()
        filePaths = glob.glob(url2pathname(self._context.resource_locator.path) + "/**/*.dat", recursive=True)
        fileNames = [os.path.basename(filePath) for filePath in filePaths]
        dateTimes = [datetime.strptime(fileName, '%Y-%m-%d_%H-%M-%S.dat') for fileName in fileNames]
        filteredDateTimes = [current for current in dateTimes if current >= begin and current < end]
        actualFileCount = len(filteredDateTimes)

        return actualFileCount / maxFileCount

    async def read_single_async(self, resourcePath: str, length: int, begin: datetime, end: datetime):

        # ############################################################################
        # Warning! This is a simplified implementation and not generally applicable! #
        # ############################################################################

        # The test files are located in a folder hierarchy where each has a length of 
        # 10-minutes (600 s):
        # ...
        # TESTDATA/DATA/test/2020-01/2020-01-02/2020-01-02_00-00-00.dat
        # TESTDATA/DATA/test/2020-01/2020-01-02/2020-01-02_00-10-00.dat
        # ...
        # The data itself is made up of progressing timestamps (unix time represented 
        # stored as 8 byte little-endian integers) with a sample rate of 1 Hz.

        # ensure the catalogs have already been loaded
        (catalog, resource, representation) = self._find(resourcePath)

        # representation ID = "1_Hz_mean" -> extract "1"
        samplesPerSecond = int(representation.Id.split("_")[0])
        secondsPerFile = 600
        typeSize = 8

        # https://stackoverflow.com/questions/111983/python-array-versus-numpy-array
        data = array('q', [0 for i in range(length)])
        dataAsBytes = memoryview(data).cast("B")
        status = array('b', [0 for i in range(length * 1)])

        # go
        currentBegin = begin

        while currentBegin < end:

            # find files
            searchPattern = url2pathname(self._context.resource_locator.path) + \
                f"/DATA/test/{currentBegin.strftime('%Y-%m')}/{currentBegin.strftime('%Y-%m-%d')}/*.dat"

            filePaths = glob.glob(searchPattern, recursive=True)

            # for each file found in target folder
            for filePath in filePaths:

                fileName = os.path.basename(filePath)
                fileBegin = datetime.strptime(fileName, '%Y-%m-%d_%H-%M-%S.dat')
                
                # if file date/time is within the limits
                if fileBegin >= currentBegin and fileBegin < end:

                    # compute target offset and file length
                    targetOffset = int((fileBegin - begin).total_seconds()) * samplesPerSecond
                    fileLength = samplesPerSecond * secondsPerFile

                    # load and copy binary data
                    with open(filePath, "rb") as file:
                        fileData = file.read()

                    for i in range(0, fileLength * typeSize):    
                        dataAsBytes[(targetOffset * typeSize) + i] = fileData[i]

                    # set status to 1 for all written data
                    for i in range(0, fileLength):    
                        status[targetOffset + i] = 1

            currentBegin += timedelta(days = 1)

        return (data, status)

    def _find(self, resourcePath):

        pathParts = resourcePath.split("/")

        representationId = pathParts[-1]
        resourceId = pathParts[-2]
        catalogId = "/" + "/".join(pathParts[1:-2])

        catalog = next((catalog for catalog in self._context.catalogs if catalog.Id == catalogId), None)

        if catalog is None:
            raise Exception(f"ResourceCatalog '{catalogId}' not found.")
        
        resource = next((resource for resource in catalog.Resources if resource.Id == resourceId), None)

        if resource is None:
            raise Exception(f"Resource '{resourceId}' not found.")

        representation = next((representation for representation in resource.Representations if representation.Id == representationId), None)

        if representation is None:
            raise Exception(f"Representation '{representationId}' not found.")

        return (catalog, resource, representation)

# get address
address = "localhost"

# get port
try:
    port = int(sys.argv[1])
except Exception as ex:
    raise Exception(f"The second command line argument must be a valid port number. Inner error: {str(ex)}")

# run
asyncio.run(RpcCommunicator(PythonDataSource(), address, port).run())
