import asyncio
from httpx import AsyncClient
import NexusClient

async def my_coroutine():
    http_client = AsyncClient(base_url="https://localhost:8443", verify=False)
    client = NexusClient.NexusClient(http_client)

    response = await client.users.get_me_async()

loop = asyncio.get_event_loop()
loop.run_until_complete(my_coroutine())