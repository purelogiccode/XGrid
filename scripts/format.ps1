#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet format XGrid.sln @args
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
