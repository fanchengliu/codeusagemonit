param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\UIAutomationTypes.dll')
$arguments = @('/nologo','/target:exe','/platform:x64','/codepage:65001','/langversion:5',('/out:'+(Join-Path $destination 'UiRegression.exe')),('/reference:'+(Join-Path $destination 'codeusagemonit.exe')))
foreach ($reference in $references) { $arguments += '/reference:'+(Join-Path $framework $reference) }
$arguments += Join-Path $PSScriptRoot 'tests\UiRegression.cs'
& (Join-Path $framework 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'UI harness compilation failed' }
$process = Start-Process -FilePath (Join-Path $destination 'UiRegression.exe') -WorkingDirectory $destination -WindowStyle Hidden -PassThru -Wait
Get-Content -LiteralPath (Join-Path $destination 'verification\ui-regression.json')
if ($process.ExitCode -ne 0) { throw 'UI regression failed' }
