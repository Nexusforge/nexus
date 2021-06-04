import asyncio
from datetime import datetime, timezone
from typing import Awaitable, List, Tuple
from uuid import uuid3

from PythonPipeExtensibility import (Catalog, Channel, Dataset, IDataSource,
                                     NexusDataType, PipeCommunicator)


class PythonDataSource(IDataSource):
	
	async def get_catalogs_async(self) -> Awaitable[List[Catalog]]:

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

	async def get_time_range_async(self) -> Awaitable[Tuple[datetime, datetime]]:
		# LOCAL_TIMEZONE = datetime.now().astimezone().tzinfo
		begin = datetime(2019, 12, 31, 12, 0, 0, 0, tzinfo = timezone.utc)
		end = datetime(2020, 1, 2, 0, 20, 0, 0, tzinfo = timezone.utc)

		return (begin, end)

	async def get_availability_async(self, begin: datetime, end: datetime) -> Awaitable[float]:
		return 2 / 144.0

	async def read_single_async(self, dataset: Dataset, length: int, begin: datetime, end: datetime) -> Awaitable[float]:
		return [1, 2, 3, 4]

async def main() -> Awaitable:
    
	communicator = PipeCommunicator(PythonDataSource())
	await communicator.run()
    
asyncio.run(main())
