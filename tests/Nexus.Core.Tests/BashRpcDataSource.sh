#!/bin/bash
# test command = dotnet test --filter BashRpcDataSourceTests ./tests/Nexus.Core.Tests/Nexus.Core.Tests.csproj

# quit on error
set -o errexit

# check prerequisites
if ! command -v jq &> /dev/null; then
    echo "I require jq but it's not installed." 1>&2
    exit
fi

# main
main() {

    # open sockets (https://admin-ahead.com/forum/general-linux/how-to-open-a-tcpudp-socket-in-a-bash-shell/)
    echo "Connecting to localhost:$1 ..."

    exec 3<>/dev/tcp/localhost/$1
    echo -ne "comm" >&3

    exec 4>/dev/tcp/localhost/$1
    echo -ne "data" >&4
  
    echo "Starting to listen for JSON-RPC messages ..."
    listen

    read dummy
}

# process incoming messages
listen() {

    while true; do

        # get json length
        read32BE json_length <&3

        # get json
        json=$(dd bs=$json_length count=1 <&3 2> /dev/null)
        tmp=$(echo $json | jq --raw-output '. | to_entries | map("[\(.key)]=\(.value)") | reduce .[] as $item ("invocation=("; . + $item + " ") + ")"')
        declare -A "$tmp"

        jsonrpc=${invocation[jsonrpc]}
        id=${invocation[id]}
        method=${invocation[method]}

        # check jsonrpc
        if [ "$jsonrpc" != "2.0" ]; then
            echo "Only JSON-RPC messages are supported." 1>&2
            exit 1
        fi

        # check id
        if [ -z "$id" ]; then 
            echo "Notifications are not supported." 1>&2
            exit 1
        fi

        # prepare response
        echo "Received invocation for method '$method'. Preparing response ..."

        if [ "$method" = "getApiVersionAsync" ]; then
            response='{ "jsonrpc": "2.0", "id": '$id', "result": { "ApiVersion": 1 } }'

        elif [ "$method" = "setContextAsync" ]; then
            response='{ "jsonrpc": "2.0", "id": '$id', "result": null }'

        elif [ "$method" = "getCatalogsAsync" ]; then
            catalogs=$(<catalogs.json)
            response='{ "jsonrpc": "2.0", "id": '$id', "result": '"$catalogs"' }'

        elif [ "$method" = "getTimeRangeAsync" ]; then
            response='{ "jsonrpc": "2.0", "id": '$id', "result": { "Begin": "2019-12-31T12:00:00Z", "End": "2020-01-02T09:50:00Z" } }'

        elif [ "$method" = "getAvailabilityAsync" ]; then
            availability=$(bc -l <<< "2/144")
            response='{ "jsonrpc": "2.0", "id": '$id', "result": { "Availability": '$availability' } }'

        elif [ "$method" = "readSingleAsync" ]; then
            response='{ "jsonrpc": "2.0", "id": '$id', "result": null }'

        else
            echo "Method '$method' is not supported." 1>&2
            exit 1

        fi

        # get response length
        local_lang=$LANG local_lc_all=$LC_ALL
        LANG=C LC_ALL=C
        byte_length=${#response}
        LANG=$local_lang LC_ALL=$local_lc_all

        # send response
        echo "Sending response ($byte_length bytes) ..."
        write $byte_length 32 "dummy" >&3
        printf "$response" >&3

        if [ "$method" = "readSingleAsync" ]; then

            # send data (86400 seconds per day * 3 days * 8 bytes)
            echo "Sending data ..."
            printf 'd%.0s' {1..2073600} >&4

            # send status
            echo "Sending status ..."
            printf 's%.0s' {1..259200} >&4

        fi

    done
}

# read and write bytes (https://stackoverflow.com/questions/13889659/read-a-file-by-bytes-in-bash)
read8() {  
    local _r8_var=${1:-OUTBIN} _r8_car LANG=C IFS=
    read -r -d '' -n 1 _r8_car
    printf -v $_r8_var %d \'$_r8_car;
}

read16BE() { 
    local _r16_var=${1:-OUTBIN} _r16_lb _r16_hb
    read8 _r16_hb
    read8 _r16_lb
    printf -v $_r16_var %d $(( _r16_hb<<8 | _r16_lb ));
}

read32BE() { 
    local _r32_var=${1:-OUTBIN} _r32_lw _r32_hw
    read16BE _r32_hw
    read16BE _r32_lw
    printf -v $_r32_var %d $(( _r32_hw<<16| _r32_lw ));
}

# Usage: write <integer> [bits:64|32|16|8] [switchto big endian]
write () { 
    local i=$((${2:-64}/8)) o= v r
    r=$((i-1))

    for ((;i--;)) {
        printf -vv '\%03o' $(( ($1>>8*(0${3+-1}?i:r-i))&255 ))
        o+=$v
    }

    printf "$o"
}

# run main
main "$@"; exit