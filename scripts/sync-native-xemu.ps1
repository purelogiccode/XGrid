# Recreate the xemu build tree from the pristine reference and apply XGrid's
# patch set. native/xemu is git-ignored and disposable: the maintained version
# of the core is (References2 baseline) + (native/xgrid-core) + (patches).
param(
    [string]$Reference = 'References2/xemu-0.8.139',
    [string]$Target = 'native/xemu',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not (Test-Path $Reference)) { throw "Reference tree not found: $Reference" }
    if (Test-Path $Target) {
        if (-not $Force) { throw "$Target already exists; pass -Force to recreate it." }
        Remove-Item -Recurse -Force $Target
    }

    robocopy $Reference $Target /E /NFL /NDL /NJH /NJS /NP /XD .git build dist | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with $LASTEXITCODE" }

    # Meson cannot subdir() outside its project, so our additions are copied in
    # as a directory of the build tree. native/xgrid-core stays the source of
    # truth; this copy is generated.
    robocopy 'native/xgrid-core' "$Target/xgrid-core" /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy (xgrid-core) failed with $LASTEXITCODE" }

    $patches = Get-ChildItem 'native/xemu-patches' -Filter '*.patch' | Sort-Object Name
    foreach ($patch in $patches) {
        # `git apply` is a plumbing command: it does not need the target to be a
        # repository, it resolves paths relative to the working directory.
        $result = & git -C $Target apply -p1 $patch.FullName 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to apply $($patch.Name): $result"
        }
        Write-Output "applied $($patch.Name)"
    }

    Write-Output "tree: $Target (baseline + $(($patches | Measure-Object).Count) patches + native/xgrid-core)"
    Write-Output 'build: pwsh -File scripts/build-native-xemu.ps1'
}
finally {
    Pop-Location
}
