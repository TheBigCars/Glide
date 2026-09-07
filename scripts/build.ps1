$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'src'
$assets = Join-Path $root 'assets'
$tools = Join-Path $root 'tools'
$dist = Join-Path $root 'dist'
$frameworkPath = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'

New-Item -ItemType Directory -Force -Path $dist | Out-Null

# Generate a fresh Windows icon so the compiler gets a valid ICO on every machine.
& "$frameworkPath\csc.exe" /nologo /target:exe "/out:$dist\GenerateIcon.exe" "/reference:$frameworkPath\System.Drawing.dll" "$tools\GenerateIcon.cs"
if ($LASTEXITCODE -ne 0) { throw 'Icon generation compilation failed.' }

& "$dist\GenerateIcon.exe" "$dist\Glide.ico"
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }

$references = @(
    "$frameworkPath\WPF\PresentationFramework.dll",
    "$frameworkPath\WPF\PresentationCore.dll",
    "$frameworkPath\WPF\WindowsBase.dll",
    "$frameworkPath\System.Xaml.dll"
)

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    '/main:App',
    "/out:$dist\Glide.exe",
    "/win32icon:$dist\Glide.ico",
    "/resource:$src\MainWindow.xaml,MainWindow.xaml",
    "/resource:$assets\superlight-2-white.png,superlight-2-white.png"
)

$arguments += $references | ForEach-Object { "/reference:$_" }
$arguments += @(
    "$src\Probe.cs",
    "$src\MouseDevice.cs",
    "$src\FirmwareUpdates.cs",
    "$src\FirmwareSupport.cs",
    "$src\Controller.cs",
    "$src\Controller.Device.cs",
    "$src\App.cs"
)

& "$frameworkPath\csc.exe" @arguments
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }

Write-Host "Built $dist\Glide.exe"
