param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$fixtureDir = Join-Path $destination 'tools'
$fixture = Join-Path $fixtureDir 'ccusage.exe'
$marker = Join-Path $fixtureDir 'offline-test-fixture.txt'
if ((Test-Path -LiteralPath $fixture) -and -not (Test-Path -LiteralPath $marker)) { throw 'Use an isolated output directory: a real history tool is present.' }
New-Item -ItemType Directory -Path $fixtureDir -Force | Out-Null
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 "/out:$fixture" "/reference:$(Join-Path $framework 'System.Web.Extensions.dll')" (Join-Path $PSScriptRoot 'tests\FakeHistory.cs')
if ($LASTEXITCODE -ne 0) { throw 'Offline history fixture compilation failed' }
[IO.File]::WriteAllText($marker, 'Only synthetic history. Do not distribute this tools directory.')
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\UIAutomationTypes.dll')
$arguments = @('/nologo','/target:exe','/platform:x64','/codepage:65001','/langversion:5',('/out:'+(Join-Path $destination 'UiRegression.exe')),('/reference:'+(Join-Path $destination 'codeusagemonit.exe')))
foreach ($reference in $references) { $arguments += '/reference:'+(Join-Path $framework $reference) }
$arguments += Join-Path $PSScriptRoot 'tests\UiRegression.cs'
& (Join-Path $framework 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'UI harness compilation failed' }
$process = Start-Process -FilePath (Join-Path $destination 'UiRegression.exe') -WorkingDirectory $destination -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath (Join-Path $destination 'verification\ui-regression.json')
if ($process.ExitCode -ne 0) { throw 'UI regression failed' }
