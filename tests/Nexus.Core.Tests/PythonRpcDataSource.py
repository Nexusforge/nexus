import asyncio
import datetime
import glob
import os
from datetime import datetime, timedelta, timezone
from urllib.request import url2pathname
from uuid import uuid3

from PythonRpcDataModel import Catalog, Channel, Dataset, NexusDataType
from PythonRpcExtensibility import IDataSource, LogLevel, RpcCommunicator


class PythonDataSource(IDataSource):
	
	async def on_parameters_set_async(self):
		
		if (self.resource_locator.scheme != "file"):
			raise Exception(f"Expected 'file' URI scheme, but got '{self.resource_locator.scheme}'.")

		self.logger.Log(LogLevel.Information, "Logging works!")

	async def get_catalogs_async(self):

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

	async def read_single_async(self, dataset: Dataset, length: int, begin: datetime, end: datetime):
		
		raise Exception("NotImplemented")


async def main():
    
	communicator = RpcCommunicator(PythonDataSource())
	await communicator.run()
    
asyncio.run(main())
