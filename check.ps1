param([switch]$Device)
$ErrorActionPreference = 'Stop'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /platform:x64 /main:Checks /out:"$PSScriptRoot\MouseChecks.exe" "$PSScriptRoot\Probe.cs" "$PSScriptRoot\MouseDevice.cs" "$PSScriptRoot\FirmwareUpdates.cs" "$PSScriptRoot\Checks.cs"
if ($LASTEXITCODE -ne 0) { throw 'Check compilation failed.' }
if ($Device) { & "$PSScriptRoot\MouseChecks.exe" --device } else { & "$PSScriptRoot\MouseChecks.exe" }
if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
