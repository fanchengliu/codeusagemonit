# codeusagemonit installer for Windows 10 / 11 (x64), no admin rights needed.
#
#   irm https://raw.githubusercontent.com/fanchengliu/codeusagemonit/main/install.ps1 | iex
#
# Downloads the latest release from GitHub, checks it against the release's SHA256SUMS.txt,
# installs to %LOCALAPPDATA%\Programs\codeusagemonit, puts codeusage on the user PATH and
# adds a Start menu shortcut. Running it again updates in place; the data folder is kept.
# Optional: $env:CODEUSAGEMONIT_DIR to install elsewhere, $env:CODEUSAGEMONIT_VERSION = '1.2.0'.

& {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $repo = 'fanchengliu/codeusagemonit'
    $dir = if ($env:CODEUSAGEMONIT_DIR) { $env:CODEUSAGEMONIT_DIR } else { Join-Path $env:LOCALAPPDATA 'Programs\codeusagemonit' }
    $headers = @{ 'User-Agent' = 'codeusagemonit-installer' }

    $api = if ($env:CODEUSAGEMONIT_VERSION) { "https://api.github.com/repos/$repo/releases/tags/v$($env:CODEUSAGEMONIT_VERSION)" } else { "https://api.github.com/repos/$repo/releases/latest" }
    Write-Host 'codeusagemonit: looking up the release...'
    $release = Invoke-RestMethod -Uri $api -Headers $headers
    $zip = $release.assets | Where-Object { $_.name -like 'codeusagemonit-*-win-x64.zip' } | Select-Object -First 1
    if (-not $zip) { throw "No Windows x64 zip in release $($release.tag_name)." }
    $sums = $release.assets | Where-Object { $_.name -eq 'SHA256SUMS.txt' -or $_.name -eq "$($zip.name).sha256" } | Select-Object -First 1

    $temp = Join-Path ([IO.Path]::GetTempPath()) ('codeusagemonit-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp | Out-Null
    try {
        $file = Join-Path $temp $zip.name
        Write-Host "codeusagemonit: downloading $($zip.name) ($([Math]::Round($zip.size / 1MB, 2)) MB)..."
        Invoke-WebRequest -Uri $zip.browser_download_url -OutFile $file -Headers $headers -UseBasicParsing
        if ($sums) {
            $list = (Invoke-WebRequest -Uri $sums.browser_download_url -Headers $headers -UseBasicParsing).Content
            if ($list -is [byte[]]) { $list = [Text.Encoding]::UTF8.GetString($list) }
            $lines = @(($list -split "`r?`n") | Where-Object { $_.Trim() })
            $line = $lines | Where-Object { $_ -match [Regex]::Escape($zip.name) } | Select-Object -First 1
            if (-not $line -and $lines.Count -eq 1) { $line = $lines[0] }
            if (-not $line) { throw "$($sums.name) lists no hash for $($zip.name)." }
            $expected = $line.Trim().Split(' ')[0].ToLowerInvariant()
            $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($expected -ne $actual) { throw "SHA-256 mismatch: expected $expected, got $actual." }
            Write-Host "codeusagemonit: SHA-256 verified ($actual)."
        } else {
            Write-Warning 'This release publishes no SHA-256 list; the download was not verified.'
        }

        # A running copy locks its files; ask it to quit first.
        $running = Get-Process -Name 'codeusagemonit' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($dir, [StringComparison]::OrdinalIgnoreCase) }
        if ($running) { Write-Host 'codeusagemonit: closing the running copy...'; $running | Stop-Process -Force; Start-Sleep -Milliseconds 600 }

        $unpacked = Join-Path $temp 'unpacked'
        Expand-Archive -LiteralPath $file -DestinationPath $unpacked -Force
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        # Everything except data\ (settings, keys, caches) is replaced.
        Get-ChildItem -LiteralPath $unpacked | Where-Object { $_.Name -ne 'data' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $dir -Recurse -Force }
    } finally {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }

    # codeusage on the user PATH.
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $parts = @(if ($userPath) { $userPath.Split(';') | Where-Object { $_ } })
    if ($parts -notcontains $dir) {
        [Environment]::SetEnvironmentVariable('Path', (($parts + $dir) -join ';'), 'User')
        $env:Path = $env:Path.TrimEnd(';') + ';' + $dir
        Write-Host "codeusagemonit: added $dir to your user PATH (new terminals pick it up)."
    }

    # Start menu shortcut.
    $programs = [Environment]::GetFolderPath('Programs')
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $programs 'codeusagemonit.lnk'))
    $link.TargetPath = Join-Path $dir 'codeusagemonit.exe'; $link.WorkingDirectory = $dir; $link.IconLocation = (Join-Path $dir 'codeusagemonit.exe') + ',0'; $link.Save()

    Write-Host ''
    Write-Host "codeusagemonit $($release.tag_name) installed to $dir" -ForegroundColor Green
    Write-Host '  Start it from the Start menu, or run: codeusagemonit'
    Write-Host '  Terminal: codeusage status, codeusage cost --days 7'
    Write-Host '  Update: run this command again.'
}
