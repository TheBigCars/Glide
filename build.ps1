$ErrorActionPreference = 'Stop'
$frameworkPath = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$references = @("$frameworkPath\WPF\PresentationFramework.dll", "$frameworkPath\WPF\PresentationCore.dll", "$frameworkPath\WPF\WindowsBase.dll", "$frameworkPath\System.Xaml.dll")
& "$frameworkPath\csc.exe" /nologo /target:exe "/out:$PSScriptRoot\GenerateIcon.exe" "/reference:$frameworkPath\System.Drawing.dll" "$PSScriptRoot\GenerateIcon.cs"
if ($LASTEXITCODE -ne 0) { throw 'Icon generation compilation failed.' }
& "$PSScriptRoot\GenerateIcon.exe" "$PSScriptRoot\Glide.ico"
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/main:App', "/out:$PSScriptRoot\Glide.exe", "/win32icon:$PSScriptRoot\Glide.ico", "/resource:$PSScriptRoot\MainWindow.xaml,MainWindow.xaml", "/resource:$PSScriptRoot\superlight-2-white.png,superlight-2-white.png")
$arguments += $references | ForEach-Object { "/reference:$_" }
$arguments += @("$PSScriptRoot\Probe.cs", "$PSScriptRoot\MouseDevice.cs", "$PSScriptRoot\FirmwareUpdates.cs", "$PSScriptRoot\FirmwareSupport.cs", "$PSScriptRoot\App.cs")
& "$frameworkPath\csc.exe" @arguments
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
