$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

New-Item -ItemType Directory -Force -Path $dist | Out-Null

& $compiler /nologo /target:exe /platform:x64 "/out:$dist\MouseProbe.exe" "$src\Probe.cs"
if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }

Write-Host "Built $dist\MouseProbe.exe"
