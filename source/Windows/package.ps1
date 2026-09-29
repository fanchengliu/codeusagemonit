param(
  [string]$AppDirectory = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
  [string]$DestinationDirectory = '',
  [switch]$Force
)
$ErrorActionPreference = 'Stop'
$appRoot = [IO.Path]::GetFullPath($AppDirectory)
if (-not $DestinationDirectory) { $DestinationDirectory = Join-Path $appRoot 'dist' }
$destination = [IO.Path]::GetFullPath($DestinationDirectory)
$sourceText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'App.cs'))
$version = [regex]::Match($sourceText, 'AssemblyVersion\("(\d+\.\d+\.\d+)\.\d+"\)').Groups[1].Value
if (-not $version) { throw 'Cannot determine application version.' }
$name = 'codeusagemonit-' + $version + '-win-x64'
$archive = Join-Path $destination ($name + '.zip')
if ((Test-Path -LiteralPath $archive) -and -not $Force) { throw 'Release already exists. Use -Force to replace this ZIP.' }
# A fresh staging directory, explicit source/asset allowlist: never copy app data or caches.
$stage = Join-Path $appRoot ('verification\package-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $stage $name
New-Item -ItemType Directory -Path $payload,$destination -Force | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $payload
foreach ($file in @('LICENSE','THIRD-PARTY-NOTICES.md','README.md','使用说明.md')) {
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $payload
}
foreach ($folder in @('tools','licenses','source\Windows','source\Windows\tests')) { New-Item -ItemType Directory -Path (Join-Path $payload $folder) -Force | Out-Null }
Copy-Item -LiteralPath (Join-Path $appRoot 'tools\ccusage.exe') -Destination (Join-Path $payload 'tools')
foreach ($nameLicense in @('ccusage-MIT.txt','CodexBar-MIT.txt','CodeZeno-MIT.txt')) {
  Copy-Item -LiteralPath (Join-Path $appRoot ('licenses\' + $nameLicense)) -Destination (Join-Path $payload 'licenses')
}
Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object { $_.Extension -in @('.cs','.xaml','.manifest','.ps1','.md') -or $_.Name -eq 'LICENSE' } | ForEach-Object {
  Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $payload 'source\Windows')
}
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -File -Filter '*.cs' | ForEach-Object {
  Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $payload 'source\Windows\tests')
}
if (@(Get-ChildItem -LiteralPath (Join-Path $payload 'icons') -File).Count -lt 11) { throw 'Provider icons missing from package.' }
$files = @(Get-ChildItem -LiteralPath $payload -File -Recurse)
$blocked = $files | Where-Object { $_.FullName.Substring($payload.Length + 1) -match '(^|\\)(data|verification|\.git)(\\|$)|\.key$|auth\.json$|settings\.json$|quota-cache\.json$' }
if ($blocked) { throw 'Private/runtime data found in staging.' }
$hashes = foreach ($file in ($files | Sort-Object FullName)) {
  (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $file.FullName.Substring($payload.Length + 1).Replace('\','/')
}
[IO.File]::WriteAllLines((Join-Path $payload 'SHA256SUMS.txt'), $hashes, [Text.Encoding]::UTF8)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporaryArchive = Join-Path $destination ($name + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryArchive, [IO.Compression.CompressionLevel]::Optimal, $false)
Move-Item -LiteralPath $temporaryArchive -Destination $archive -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($archive + '.sha256'), ($hash + '  ' + [IO.Path]::GetFileName($archive) + [Environment]::NewLine), [Text.Encoding]::ASCII)
Write-Output $archive
