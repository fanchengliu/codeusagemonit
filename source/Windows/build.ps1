param([string]$OutputDirectory = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$ErrorActionPreference = 'Stop'
$compilerRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $compilerRoot 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler not found.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Add-Type -AssemblyName System.Drawing

# App/tray icon: a multi-size 32-bit ICO (16-64 px as DIB frames, 256 px as PNG).
# Icon.Save() on a GetHicon() handle only writes a 4-bit, 16-colour frame.
function New-IconBitmap([int]$s) {
  $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)
  $d = [Math]::Max(4, [Math]::Round($s * 0.44)); $e = $s - 1
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($e - $d, 0, $d, $d, 270, 90); $path.AddArc($e - $d, $e - $d, $d, $d, 0, 90); $path.AddArc(0, $e - $d, $d, $d, 90, 90); $path.CloseFigure()
  $background = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 30, 33, 39))
  $g.FillPath($background, $path); $background.Dispose(); $path.Dispose()
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
  $w = [Math]::Max(2, [Math]::Round($s * 0.15)); $gap = [Math]::Max(1, [Math]::Round($s * 0.07))
  $x = [Math]::Floor(($s - (3 * $w + 2 * $gap)) / 2); $base = [Math]::Round($s * 0.79)
  foreach ($bar in @(@(0.30, 109, 224, 214), @(0.56, 150, 217, 238), @(0.42, 214, 193, 249))) {
    $h = [Math]::Max(2, [Math]::Round($s * $bar[0]))
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, $bar[1], $bar[2], $bar[3]))
    $g.FillRectangle($brush, [int]$x, [int]($base - $h), [int]$w, [int]$h); $brush.Dispose(); $x += $w + $gap
  }
  $g.Dispose(); return $bmp
}
function Get-DibFrame($bmp) {
  $s = $bmp.Width; $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
  $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $raw = New-Object byte[] ($data.Stride * $s); [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $raw, 0, $raw.Length); $stride = $data.Stride; $bmp.UnlockBits($data)
  $maskStride = [int]([Math]::Floor(($s + 31) / 32) * 4)
  $ms = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter($ms)
  $bw.Write([int]40); $bw.Write([int]$s); $bw.Write([int]($s * 2)); $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
  $bw.Write([int]($s * $s * 4 + $maskStride * $s)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
  for ($y = $s - 1; $y -ge 0; $y--) { $bw.Write($raw, $y * $stride, $s * 4) }
  $bw.Write((New-Object byte[] ($maskStride * $s))); $bw.Flush(); return ,$ms.ToArray()
}
$frames = @()
foreach ($size in @(16, 20, 24, 32, 40, 48, 64, 256)) {
  $bmp = New-IconBitmap $size
  if ($size -eq 256) { $png = New-Object System.IO.MemoryStream; $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png); $bytes = $png.ToArray() } else { $bytes = Get-DibFrame $bmp }
  $bmp.Dispose(); $frames += ,@($size, $bytes)
}
$iconPath = Join-Path $OutputDirectory 'app.ico'
$ico = New-Object System.IO.MemoryStream; $writer = New-Object System.IO.BinaryWriter($ico)
$writer.Write([int16]0); $writer.Write([int16]1); $writer.Write([int16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) { $dim = if ($frame[0] -ge 256) { 0 } else { $frame[0] }; $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([int16]1); $writer.Write([int16]32); $writer.Write([int]$frame[1].Length); $writer.Write([int]$offset); $offset += $frame[1].Length }
foreach ($frame in $frames) { $writer.Write([byte[]]$frame[1]) }
$writer.Flush(); [System.IO.File]::WriteAllBytes($iconPath, $ico.ToArray())

$references = @('System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Security.dll','System.Management.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Select-Object -ExpandProperty FullName)
# Two executables from the same sources: the desktop app and the codeusage CLI.
foreach ($target in @(@('winexe', 'codeusagemonit.exe', 'CodeUsageMonit.Program'), @('exe', 'codeusage.exe', 'CodeUsageMonit.CliProgram'))) {
  $arguments = @('/nologo',('/target:'+$target[0]),'/platform:x64','/optimize+','/utf8output','/codepage:65001','/langversion:5',('/main:'+$target[2]),('/out:'+(Join-Path $OutputDirectory $target[1])),('/win32icon:'+$iconPath),('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')))
  foreach($reference in $references) { $arguments += '/reference:'+(Join-Path $compilerRoot $reference) }
  $arguments += $sources
  & $compiler @arguments
  if ($LASTEXITCODE -ne 0) { throw ('C# build failed: ' + $target[1]) }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Panel.xaml') -Destination (Join-Path $OutputDirectory 'Panel.xaml') -Force
$iconDir = Join-Path $OutputDirectory 'icons'
New-Item -ItemType Directory -Path $iconDir -Force | Out-Null
# App provider id -> upstream CodexBar icon name (ZCode uses the z.ai / GLM mark).
$iconMap = [ordered]@{ codex='codex'; claude='claude'; cursor='cursor'; antigravity='antigravity'; deepseek='deepseek'; grok='grok'; copilot='copilot'; kimi='kimi'; opencode='opencode'; zcode='zai'; pi='pi' }
foreach($provider in $iconMap.Keys) {
  $sourceIcon = Join-Path (Split-Path $PSScriptRoot -Parent) ('Sources\CodexBar\Resources\ProviderIcon-'+$iconMap[$provider]+'.svg')
  if (-not (Test-Path -LiteralPath $sourceIcon)) {
    # The portable release contains the Windows source and licensed SVG assets.
    $sourceIcon = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) ('icons\'+$provider+'.svg')
  }
  $targetIcon = Join-Path $iconDir ($provider+'.svg')
  if ((Test-Path -LiteralPath $sourceIcon) -and [IO.Path]::GetFullPath($sourceIcon) -ne [IO.Path]::GetFullPath($targetIcon)) { Copy-Item -LiteralPath $sourceIcon -Destination $targetIcon -Force }
}
Write-Output ('Built '+(Join-Path $OutputDirectory 'codeusagemonit.exe'))
