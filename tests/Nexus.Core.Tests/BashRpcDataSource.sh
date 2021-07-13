#!/bin/bash
# dotnet test --filter BashRpcDataSourceTests.CanProvideTimeRange ./tests/Nexus.Core.Tests/Nexus.Core.Tests.csproj
# https://admin-ahead.com/forum/general-linux/how-to-open-a-tcpudp-socket-in-a-bash-shell/

# https://stackoverflow.com/questions/13889659/read-a-file-by-bytes-in-bash
read8() {  local _r8_var=${1:-OUTBIN} _r8_car LANG=C IFS=
    read -r -d '' -n 1 _r8_car
    printf -v $_r8_var %d \'$_r8_car;
}

read16BE() { local _r16_var=${1:-OUTBIN} _r16_lb _r16_hb
    read8 _r16_hb
    read8 _r16_lb
    printf -v $_r16_var %d $(( _r16_hb<<8 | _r16_lb ));
}

read32BE() { local _r32_var=${1:-OUTBIN} _r32_lw _r32_hw
    read16BE _r32_hw
    read16BE _r32_lw
    printf -v $_r32_var %d $(( _r32_hw<<16| _r32_lw ));
}

# open comm and data connections
echo "Connecting to localhost:$1 ..."

exec 3<>/dev/tcp/localhost/$1
echo -ne "comm" >&3

exec 4>/dev/tcp/localhost/$1
echo -ne "data" >&4

echo "Connecting to localhost:$1 ... Done."

# get message length
read32BE messageLength <&3

# get message
message=$(dd bs=$messageLength count=1 <&3 2> /dev/null)
method=$(echo $message | jq -r '.method')

echo "Method = ${method}."

read dummy