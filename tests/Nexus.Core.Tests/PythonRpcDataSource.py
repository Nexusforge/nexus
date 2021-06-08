import asyncio
import datetime
import glob
import os
from array import array
from datetime import datetime, timedelta, timezone
from urllib.request import url2pathname
from uuid import uuid3

from PythonRpcDataModel import Catalog, Channel, Dataset, NexusDataType
from PythonRpcExtensibility import IDataSource, LogLevel, RpcCommunicator

class NULL_NAMESPACE:
    bytes = b''

class PythonDataSource(IDataSource):
    
    _catalogs = None

    async def on_parameters_set_async(self):
        
        if (self.resource_locator.scheme != "file"):
            raise Exception(f"Expected 'file' URI scheme, but got '{self.resource_locator.scheme}'.")

        self.logger.Log(LogLevel.Information, "Logging works!")

    async def get_catalogs_async(self):

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
        self._catalogs = [catalog1, catalog2]
        return self._catalogs

    async def get_time_range_async(self, catalogId: str):

        if catalogId != "/A/B/C":
            raise Exception("Unknown catalog ID.")

        filePaths = glob.glob(url2pathname(self.resource_locator.path) + "/**/*.dat", recursive=True)
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
        filePaths = glob.glob(url2pathname(self.resource_locator.path) + "/**/*.dat", recursive=True)
        fileNames = [os.path.basename(filePath) for filePath in filePaths]
        dateTimes = [datetime.strptime(fileName, '%Y-%m-%d_%H-%M-%S.dat') for fileName in fileNames]
        filteredDateTimes = [current for current in dateTimes if current >= begin and current < end]
        actualFileCount = len(filteredDateTimes)

        return actualFileCount / maxFileCount

    async def read_single_async(self, channelPath: str, length: int, begin: datetime, end: datetime):

        # ############################################################################
        # Warning! This is a simplified implementation and not generally applicable! #
        # ############################################################################

        # The test files are located in a folder hierarchy and have an individual 
        # length of 10-minutes (600 s):
        # ...
        # TESTDATA/DATA/test/2020-01/2020-01-02/2020-01-02_00-00-00.dat
        # TESTDATA/DATA/test/2020-01/2020-01-02/2020-01-02_00-10-00.dat
        # ...
        # The data itself is made up of progressing timestamps (unix time represented 
        # stored as 8 byte little-endian integers) with a sample rate of 1 Hz.

        # ensure the catalogs have already been loaded
        await self._ensureCatalogsAsync()

        (catalog, channel, dataset) = self._find(channelPath)

        # dataset ID = "1 Hz_mean" -> extract "1"
        samplesPerSecond = int(dataset.Id.split(" ")[0])
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
            searchPattern = url2pathname(self.resource_locator.path) + \
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

    async def _ensureCatalogsAsync(self):
        if (self._catalogs is None):
            await self.get_catalogs_async()

    def _find(self, channelPath):

        pathParts = channelPath.split("/")

        datasetId = pathParts[-1]
        channelId = pathParts[-2]
        catalogId = "/" + "/".join(pathParts[1:-2])

        catalog = next((catalog for catalog in self._catalogs if catalog.Id == catalogId), None)

        if catalog is None:
            raise Exception(f"Catalog '{catalogId}' not found.")
        
        channel = next((channel for channel in catalog.Channels if channel.Id == channelId), None)

        if channel is None:
            raise Exception(f"Channel '{channelId}' not found.")

        dataset = next((dataset for dataset in channel.Datasets if dataset.Id == datasetId), None)

        if dataset is None:
            raise Exception(f"Dataset '{datasetId}' not found.")

        return (catalog, channel, dataset)

asyncio.run(RpcCommunicator(PythonDataSource()).run())
