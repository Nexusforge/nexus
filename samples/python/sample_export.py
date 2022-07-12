# Requires:
# pip install aiohttp
# pip install asyncio

import asyncio
from datetime import datetime, timedelta, timezone

# settings
scheme = "http"
host = "localhost"
port = 5000
username = "test@nexus.org"
password = "#test0/User1" # password = input("Please enter your password: ")
target_folder = "data"

begin = datetime(2020, 2, 1, 0, 0, tzinfo=timezone.utc)
end   = datetime(2020, 2, 2, 0, 0, tzinfo=timezone.utc)

# must all be of the same sample rate
resource_paths = [
    "/IN_MEMORY/TEST/ACCESSIBLE/T1/1 s_mean",
    "/IN_MEMORY/TEST/ACCESSIBLE/V1/1 s_mean"
]

# load connector script
import sys, os, urllib.request, tempfile
connectorFolderPath = os.path.join(tempfile.gettempdir(), "Nexus")
os.makedirs(connectorFolderPath, exist_ok=True)
url = f"{scheme}://{host}:{port}/connectors/NexusConnector.py"
urllib.request.urlretrieve(url, connectorFolderPath + "/NexusConnector.py")
sys.path.append(connectorFolderPath)
NexusConnector = ""
exec("from NexusConnector import NexusConnector")

# export data
connector = NexusConnector(scheme, host, port, username, password)
# without authentication: connector = NexusConnector(scheme, host, port)

params = {
    "FileGranularity": "Day",       # Minute_1, Minute_10, Hour, Day, SingleFile
    "FileFormat": "MAT73",          # CSV, FAMOS, MAT73
    "ResourcePaths": resource_paths,

    # CSV-only:
    "Configuration": {
        "SignificantFigures": "4",
        "RowIndexFormat": "Index" # Index, Unix, Excel
    }
}

asyncio.run(connector.export(begin, end, params, target_folder))
