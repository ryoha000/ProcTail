#!/bin/bash

set -e

dotnet publish src/ProcTail.Host/ -c Release -r win-x64 --self-contained
dotnet publish src/ProcTail.Cli/ -c Release -r win-x64 --self-contained

cp -r src/ProcTail.Host/bin/Release/net8.0/win-x64/* /mnt/f/workspace/tmp/proctail/cli
cp -r src/ProcTail.Cli/bin/Release/net8.0/win-x64/* /mnt/f/workspace/tmp/proctail/cli
