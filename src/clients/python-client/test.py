import asyncio
from datetime import datetime, timedelta
from httpx import AsyncClient
import NexusClient

async def my_coroutine():

    http_client = AsyncClient(base_url="https://localhost:8443", verify=False)

    token_pair = NexusClient.TokenPair(\
        "eyJhbGciOiJodHRwOi8vd3d3LnczLm9yZy8yMDAxLzA0L3htbGRzaWctbW9yZSNobWFjLXNoYTI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJmOTIwOGY1MC1jZDU0LTQxNjUtODA0MS1iNWNkMTlhZjQ1YTRAbmV4dXMiLCJuYW1lIjoiU3RhciBMb3JkIiwiSXNBZG1pbiI6InRydWUiLCJleHAiOjE2NDYwNDQ4ODAsIm5iZiI6MTY0NjA0NDgyMCwiaWF0IjoxNjQ2MDQ0ODIwfQ.rM0I03rNevjXntTrOFIxBlJOOXeHCxTBL-4HVuPfiAg", \
        "f9208f50-cd54-4165-8041-b5cd19af45a4%40nexus@smtikwhZVn1WbxDuG51czYCVG4mZWH3e6lpAWhhpMouRBq2wfQCpD9LxXI44IV3nrpXZHoHoHnrZ50Hp9vcY5g==")

    client = NexusClient.NexusClient(http_client)
    client.sign_in(token_pair)

    begin = datetime(2021, 1, 1, 12, 00, 10)
    end = datetime(2021, 1, 1, 12, 10, 10)
    file_period = timedelta(days=1)
    type = "Nexus.Writers.Csv"
    resource_paths = ["/IN_MEMORY/TEST/ACCESSIBLE/T1/1_s_mean"]

    parameters = NexusClient.ExportParameters(begin, end, file_period, type, resource_paths, {})
    # response1 = await client.jobs.export_async(parameters)
    response2 = await client.users.get_me_async()
    b = 1

loop = asyncio.get_event_loop()
loop.run_until_complete(my_coroutine())