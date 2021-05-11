# Requires:
# pip install aiohttp
# pip install asyncio

import asyncio
from datetime import datetime, timedelta, timezone

from NexusConnector import NexusConnector

# settings
scheme = "http"
host = "localhost"
port = 8080
username = "test@nexus.org"
password = "#test0/User1" # password = input("Please enter your password: ")
target_folder = "data"

begin = datetime(2020, 2, 1, 0, 0, tzinfo=timezone.utc)
end   = datetime(2020, 2, 2, 0, 0, tzinfo=timezone.utc)

# must all be of the same sample rate
channel_paths = [
    "/IN_MEMORY/TEST/ACCESSIBLE/T1/1 s_mean",
    "/IN_MEMORY/TEST/ACCESSIBLE/V1/1 s_mean"
]

# export data
connector = NexusConnector(scheme, host, port, username, password) # or without authentication: ... = NexusConnector(host, port, secure)
# without authentication: connector = NexusConnector(scheme, host, port)

params = {
    "FileGranularity": "Day",   # Minute_1, Minute_10, Hour, Day, SingleFile
    "FileFormat": "MAT73",      # CSV, FAMOS, MAT73
    "ChannelPaths": channel_paths
}

asyncio.run(connector.export(begin, end, params, target_folder))
