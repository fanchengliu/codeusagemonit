# Used by codeusagemonit setup.exe (Inno Setup). ASCII on purpose so Windows PowerShell 5.1
# can run it without a BOM. Actions: stop | add-path | remove-path | is-link.
param(
    [Parameter(Mandatory = $true)][ValidateSet('stop', 'add-path', 'remove-path', 'is-link')][string]$Action,
    [Parameter(Mandatory = $true)][string]$Directory,
    [string]$Target = ''
)
$ErrorActionPreference = 'Stop'

function Normalize-Dir([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return '' }
    return $Path.Trim().TrimEnd('\')
}

# C:\ is a legal install directory (AllowRootDirectory). Keep the slash: a PATH
# entry of "C:" means "current directory on C:", not the drive root.
function Test-SafeDir([string]$Path) {
    if ($Path -match '^[A-Za-z]:$') { return $true }
    return $Path.Length -ge 3
}

function Path-Entry([string]$Path) {
    if ($Path -match '^[A-Za-z]:$') { return $Path + '\' }
    return $Path
}

function Process-Path($Process) {
    try { return [string]$Process.Path } catch { return '' }
}

function Broadcast-Environment {
    if (-not ('CodeUsageMonitSetup.EnvBroadcast' -as [type])) {
        Add-Type -Namespace CodeUsageMonitSetup -Name EnvBroadcast -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError=true, CharSet=System.Runtime.InteropServices.CharSet.Auto)]
public static extern System.IntPtr SendMessageTimeout(System.IntPtr hWnd, int Msg, System.UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out System.UIntPtr lpdwResult);
'@
    }
    $result = [UIntPtr]::Zero
    [CodeUsageMonitSetup.EnvBroadcast]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [UIntPtr]::Zero, 'Environment', 2, 5000, [ref]$result) | Out-Null
}

function Get-UserPathState {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Environment')
    $kind = [Microsoft.Win32.RegistryValueKind]::ExpandString
    $value = ''
    try {
        $kind = $key.GetValueKind('Path')
        $raw = $key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if ($null -ne $raw) { $value = [string]$raw }
    } catch {
        $kind = [Microsoft.Win32.RegistryValueKind]::ExpandString
        $value = ''
    }
    if ($kind -ne [Microsoft.Win32.RegistryValueKind]::ExpandString -and $kind -ne [Microsoft.Win32.RegistryValueKind]::String) {
        $kind = [Microsoft.Win32.RegistryValueKind]::ExpandString
    }
    return @{ Key = $key; Kind = $kind; Value = $value }
}

$dir = Normalize-Dir $Directory
if (-not (Test-SafeDir $dir)) { throw "Refusing to use a path this short: $dir" }

if ($Action -eq 'is-link') {
    $target = Normalize-Dir $Target
    if (-not (Test-SafeDir $target)) { exit 1 }
    if (-not (Test-Path -LiteralPath $target)) { exit 2 }
    $item = Get-Item -LiteralPath $target -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { exit 0 }
    exit 2
}

if ($Action -eq 'stop') {
    $prefix = $dir + '\'
    function Running-Here {
        @(Get-Process -Name 'codeusagemonit', 'codeusage' -ErrorAction SilentlyContinue |
            Where-Object { $p = Process-Path $_; $p -and $p.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
    }
    $running = @(Running-Here | Where-Object { $_.ProcessName -eq 'codeusagemonit' })
    $exe = $prefix + 'codeusagemonit.exe'
    if ($running.Count -gt 0 -and (Test-Path -LiteralPath $exe)) {
        # --quit signals the running instance and returns. Do not -Wait: a GUI-subsystem
        # exe that failed to exit would stall the installer.
        try { Start-Process -FilePath $exe -ArgumentList '--quit' -WindowStyle Hidden } catch { }
        Start-Sleep -Milliseconds 800
    }
    $left = @(Running-Here)
    if ($left.Count -gt 0) {
        $left | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 400
    }
    if (@(Running-Here).Count -gt 0) { exit 1 }
    exit 0
}

$target = Path-Entry (Normalize-Dir $dir)
$normTarget = Normalize-Dir $target
$state = Get-UserPathState
try {
    $raw = $state.Value
    if ($null -eq $raw) { $raw = '' }
    # Keep every other character, including empty segments from a trailing ";".
    $segments = @($raw.Split([char]';'))
    $kept = New-Object System.Collections.Generic.List[string]
    $found = $false
    foreach ($part in $segments) {
        $norm = Normalize-Dir $part
        if (($norm.Length -gt 0) -and ($norm -ieq $normTarget)) {
            $found = $true
            if ($Action -eq 'remove-path') { continue }
        }
        [void]$kept.Add($part)
    }
    if ($Action -eq 'add-path') {
        if ($found) { exit 0 }
        if ([string]::IsNullOrWhiteSpace($raw)) { $updated = $target }
        elseif ($raw.EndsWith(';')) { $updated = $raw + $target + ';' }
        else { $updated = $raw + ';' + $target }
    } else {
        if (-not $found) { exit 0 }
        $updated = [System.String]::Join(';', $kept.ToArray())
    }
    $state.Key.SetValue('Path', $updated, $state.Kind)
} finally {
    $state.Key.Close()
}
Broadcast-Environment
exit 0
