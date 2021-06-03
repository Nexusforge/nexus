import sys
import json
import array

# It is very important to read the correct number of bytes.
# simple buffer.read() will return data but subsequent buffer.write
# will fail with error 22.

data = sys.stdin.buffer.read(50)
jsonString = data.decode('utf8')
calculateRequest = json.loads(data)

# response
calculateResponse = {
  "Command": calculateRequest["Command"],
  "Address": calculateRequest["Address"],
  "Status": True,
  "Data": [1, 2, 3]
}

jsonString = json.dumps(calculateResponse)
data = bytes(jsonString, "utf-8")

try:
	sys.stdout.buffer.write(data)
	sys.stdout.flush()

except Exception as exception:
	with open("D:/Git/Test/namedpipes/namedpipeserver/error.txt", "w") as text_file:
		import traceback
		print(traceback.format_exc(), file=text_file)
		
	