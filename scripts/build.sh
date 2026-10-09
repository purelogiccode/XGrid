#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")/.."
exec dotnet build XGrid.sln -c Release "$@"
