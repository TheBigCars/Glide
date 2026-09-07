$ErrorActionPreference = 'Stop'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /platform:x64 /out:"$PSScriptRoot\MouseProbe.exe" "$PSScriptRoot\Probe.cs"
if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }
