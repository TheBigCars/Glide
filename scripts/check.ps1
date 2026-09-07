param([switch]$Device)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'src'
$tests = Join-Path $root 'tests'
$dist = Join-Path $root 'dist'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

New-Item -ItemType Directory -Force -Path $dist | Out-Null

& $compiler /nologo /target:exe /platform:x64 /main:Checks "/out:$dist\MouseChecks.exe" "$src\Probe.cs" "$src\MouseDevice.cs" "$src\FirmwareUpdates.cs" "$tests\Checks.cs"
if ($LASTEXITCODE -ne 0) { throw 'Check compilation failed.' }

if ($Device) {
    & "$dist\MouseChecks.exe" --device
} else {
    & "$dist\MouseChecks.exe"
}

if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
