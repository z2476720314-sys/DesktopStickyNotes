param([string]$Exe = (Join-Path $PSScriptRoot 'dist\DesktopStickyNotes.exe'), [int]$InspectProcessId = 0, [string]$BuildDirectory = (Join-Path $PSScriptRoot '.build'))
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Exe)) { throw 'FAIL: transparent floating EXE has not been built' }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
New-Item -ItemType Directory -Path $BuildDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $BuildDirectory 'evidence') -Force | Out-Null
$references = @('/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Xml.Linq.dll', '/reference:System.Core.dll', '/reference:System.Xml.dll')
$references += @('UIAutomationClient', 'UIAutomationTypes', 'WindowsBase') | ForEach-Object { '/reference:' + (Join-Path $framework "WPF\$_.dll") }
& "$framework\csc.exe" /nologo /warnaserror /target:exe "/out:$buildDirectory\UiChecks.exe" @references "$PSScriptRoot\UiChecks.cs"
if ($LASTEXITCODE -ne 0) { throw 'UI check compilation failed.' }
if ($InspectProcessId -gt 0) { & "$buildDirectory\UiChecks.exe" --inspect $InspectProcessId }
else { & "$buildDirectory\UiChecks.exe" $Exe $buildDirectory }
if ($LASTEXITCODE -ne 0) { throw 'Native UI checks failed.' }
