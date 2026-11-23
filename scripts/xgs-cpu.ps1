# xgs-cpu.ps1 - read the CPU registers out of an XGrid .xgs snapshot.
#
# The container is XGS1: magic, uint32 version, int32 section count, then
# [string tag][uint64 length][payload]. The CPU payload starts with uint32
# version, eight uint32 GPRs (EAX, ECX, EDX, EBX, ESP, EBP, ESI, EDI), EIP and
# EFLAGS. This tool is read-only and prints one JSON object (or a table).
#
# Usage:
#   pwsh -File scripts/xgs-cpu.ps1 -Path wt-data\...\state.xgs
#   pwsh -File scripts/xgs-cpu.ps1 -Path state.xgs -Dump 0x138D0 -DumpLength 64

param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$Dump = "",
    [int]$DumpLength = 64,
    [switch]$Json
)

$ErrorActionPreference = "Stop"
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
if (-not (Test-Path -LiteralPath $Path)) { Write-Error "Missing snapshot: $Path"; exit 2 }

$fs = [System.IO.File]::OpenRead((Resolve-Path -LiteralPath $Path).Path)
try {
    $br = [System.IO.BinaryReader]::new($fs)
    $magic = [System.Text.Encoding]::ASCII.GetString($br.ReadBytes(4))
    if ($magic -ne "XGS1") { Write-Error "Not an XGS1 snapshot (magic '$magic')"; exit 2 }
    $version = $br.ReadUInt32()
    $count = $br.ReadInt32()
    $cpu = $null
    $ram = $null
    for ($i = 0; $i -lt $count; $i++) {
        $tag = $br.ReadString()
        $len = [int64]$br.ReadUInt64()
        if ($tag -eq "CPU") {
            $cpu = $br.ReadBytes($len)
        } elseif ($tag -eq "RAM" -and $Dump) {
            $ram = $br.ReadBytes($len)
        } else {
            [void]$fs.Seek($len, [System.IO.SeekOrigin]::Current)
        }
    }
} finally {
    $fs.Dispose()
}

if ($null -eq $cpu) { Write-Error "No CPU section"; exit 2 }

$ms = [System.IO.MemoryStream]::new($cpu)
$cr = [System.IO.BinaryReader]::new($ms)
$cpuVersion = $cr.ReadUInt32()
$names = @("EAX", "ECX", "EDX", "EBX", "ESP", "EBP", "ESI", "EDI")
$gpr = [ordered]@{}
for ($i = 0; $i -lt 8; $i++) { $gpr[$names[$i]] = $cr.ReadUInt32() }
$eip = $cr.ReadUInt32()
$eflags = $cr.ReadUInt32()

$result = [ordered]@{
    path = (Resolve-Path -LiteralPath $Path).Path
    containerVersion = $version
    cpuVersion = $cpuVersion
    gpr = $gpr
    eip = ("0x{0:X8}" -f $eip)
    eflags = ("0x{0:X8}" -f $eflags)
}

# Optional: dump guest RAM at a *physical* address. The linear->physical walk is
# the emulator's job; pass a physical address (the probe report names it).
if ($Dump -and $null -ne $ram) {
    $addr = [Convert]::ToUInt64($Dump, 16)
    $n = [Math]::Min($DumpLength, $ram.Length - [int]$addr)
    if ($addr -lt [uint64]$ram.Length -and $n -gt 0) {
        $bytes = $ram[[int]$addr..([int]$addr + $n - 1)]
        $result["dumpAddress"] = ("0x{0:X8}" -f $addr)
        $result["dumpBytes"] = (($bytes | ForEach-Object { $_.ToString("X2") }) -join " ")
    } else {
        $result["dumpAddress"] = "out-of-range"
    }
}

if ($Json) {
    $result | ConvertTo-Json -Depth 4
} else {
    "snapshot: $($result.path) (container v$version, cpu v$cpuVersion)"
    foreach ($k in $names) { "  {0}=0x{1:X8}" -f $k, $result.gpr[$k] }
    "  EIP=$($result.eip) EFLAGS=$($result.eflags)"
    if ($result.Contains("dumpBytes")) { "  [$($result.dumpAddress)] $($result.dumpBytes)" }
}
exit 0
