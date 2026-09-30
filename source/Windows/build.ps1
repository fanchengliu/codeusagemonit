param(
  [string]$OutputDirectory = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
  [switch]$Installer
)
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
# API list prices used for the cost estimates (compiled from the LiteLLM / models.dev catalogs).
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'pricing.json') -Destination (Join-Path $OutputDirectory 'pricing.json') -Force
$iconDir = Join-Path $OutputDirectory 'icons'
New-Item -ItemType Directory -Path $iconDir -Force | Out-Null
# Provider logos (single-colour SVG paths, tinted in the app; source in THIRD-PARTY-NOTICES.md).
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'icons') -Filter '*.svg' | Copy-Item -Destination $iconDir -Force
Write-Output ('Built '+(Join-Path $OutputDirectory 'codeusagemonit.exe'))

function Find-InnoCompiler {
  $candidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
  )
  foreach ($candidate in $candidates) { if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate } }
  $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
  if ($command) { return $command.Source }
  throw 'Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6.3 or newer from https://jrsoftware.org/isdl.php and run build.ps1 -Installer again.'
}
function Get-AppVersion {
  $source = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'App.cs') -Raw -Encoding UTF8
  if ($source -notmatch 'AssemblyVersion\("(\d+\.\d+\.\d+)\.\d+"\)') { throw 'AssemblyVersion was not found in App.cs.' }
  return $Matches[1]
}
function New-PortableZip([string]$OutDir, [string]$RepoRoot, [string]$Version) {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $stage = Join-Path ([System.IO.Path]::GetTempPath()) ('codeusagemonit-zip-' + [Guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $stage | Out-Null
  try {
    foreach ($name in @('codeusagemonit.exe', 'codeusage.exe', 'app.ico', 'Panel.xaml', 'pricing.json')) {
      Copy-Item -LiteralPath (Join-Path $OutDir $name) -Destination (Join-Path $stage $name)
    }
    $iconStage = Join-Path $stage 'icons'
    New-Item -ItemType Directory -Path $iconStage | Out-Null
    Copy-Item -Path (Join-Path $OutDir 'icons\*') -Destination $iconStage -Recurse
    foreach ($name in @('LICENSE', 'README.md', '使用说明.md', 'THIRD-PARTY-NOTICES.md', 'install.ps1')) {
      Copy-Item -LiteralPath (Join-Path $RepoRoot $name) -Destination (Join-Path $stage $name)
    }
    Copy-Item -LiteralPath (Join-Path $RepoRoot 'source') -Destination (Join-Path $stage 'source') -Recurse
    if (Test-Path -LiteralPath (Join-Path $stage 'installed.txt')) { throw 'Portable zip must not ship installed.txt next to the executable.' }
    $zip = Join-Path $OutDir ('codeusagemonit-' + $Version + '-win-x64.zip')
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    return $zip
  } finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
  }
}
if ($Installer) {
  $out = [System.IO.Path]::GetFullPath($OutputDirectory)
  $repoRoot = [System.IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
  $version = Get-AppVersion
  foreach ($name in @('codeusagemonit.exe', 'codeusage.exe', 'app.ico', 'Panel.xaml', 'pricing.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $out $name))) { throw ('Missing build output: ' + $name) }
  }
  $zip = New-PortableZip $out $repoRoot $version
  $iscc = Find-InnoCompiler
  $iss = Join-Path $PSScriptRoot 'installer\codeusagemonit.iss'
  $isl = Join-Path $PSScriptRoot 'installer\ChineseSimplified.isl'
  if (-not (Test-Path -LiteralPath $isl)) { throw 'Missing source\Windows\installer\ChineseSimplified.isl.' }
  $buildDirArg = '/DBuildDir="' + ($out -replace '"', '') + '"'
  $repoArg = '/DRepoRoot="' + ($repoRoot -replace '"', '') + '"'
  & $iscc ('/DMyAppVersion=' + $version) $buildDirArg $repoArg $iss
  if ($LASTEXITCODE -ne 0) { throw ('Inno Setup compile failed (' + $LASTEXITCODE + ').') }
  $setup = Join-Path $out ('codeusagemonit-setup-' + $version + '.exe')
  if (-not (Test-Path -LiteralPath $setup)) { throw ('Installer was not written to ' + $setup) }
  $sums = Join-Path $out 'SHA256SUMS.txt'
  $lines = @(
    ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLower() + '  ' + [System.IO.Path]::GetFileName($zip)),
    ((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLower() + '  ' + [System.IO.Path]::GetFileName($setup))
  )
  [System.IO.File]::WriteAllLines($sums, $lines, (New-Object System.Text.UTF8Encoding $false))
  Write-Output ('Built ' + $setup)
  Write-Output ('Built ' + $zip)
  Write-Output ('Built ' + $sums)
}
