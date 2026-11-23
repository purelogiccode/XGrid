#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet test XGrid.sln -c Release @args
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
