param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'), [string]$BuildDirectory = (Join-Path $PSScriptRoot '.build'))
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory, $BuildDirectory -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler is unavailable.' }
$references = @('PresentationFramework', 'PresentationCore', 'WindowsBase') | ForEach-Object { '/reference:' + (Join-Path $framework "WPF\$_.dll") }
$references += '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Xml.dll', '/reference:System.Core.dll', '/reference:System.Xaml.dll'
$target = Join-Path $OutputDirectory 'DesktopStickyNotes.exe'
& "$PSScriptRoot\make-icon.ps1"
$icon = Join-Path $PSScriptRoot 'Assets\app.ico'
& $compiler /nologo /warnaserror /optimize+ /target:winexe /platform:anycpu "/out:$target" "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$icon" "/resource:$icon,AppIcon" @references "$PSScriptRoot\Core.cs" "$PSScriptRoot\ImageStore.cs" "$PSScriptRoot\ImagePreviewWindow.cs" "$PSScriptRoot\NoteWindow.cs" "$PSScriptRoot\Program.cs"
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Copy-Item -LiteralPath $icon -Destination (Join-Path $OutputDirectory 'app.ico')
& $compiler /nologo /warnaserror /target:exe '/reference:System.Xml.Linq.dll' '/reference:System.Core.dll' "/out:$BuildDirectory\Checks.exe" "$PSScriptRoot\Core.cs" "$PSScriptRoot\Checks.cs"
if ($LASTEXITCODE -ne 0) { throw 'Check compilation failed.' }
& "$BuildDirectory\Checks.exe" $BuildDirectory
if ($LASTEXITCODE -ne 0) { throw 'Persistence checks failed.' }
Write-Output "Built: $target"
