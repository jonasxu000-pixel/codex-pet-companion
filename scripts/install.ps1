[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$sourceExe = Join-Path $PSScriptRoot 'CodexPetCompanion.exe'
if (-not (Test-Path -LiteralPath $sourceExe)) { throw 'Extract the release ZIP before running install.cmd.' }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($sourceExe).FileVersion
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Invalid release version.' }
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs\CodexPetCompanion'
$destination = Join-Path $installRoot $version
$targetExe = Join-Path $destination 'CodexPetCompanion.exe'
$legacyKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
try { $oldCommand = if ($null -ne $legacyKey) { $legacyKey.GetValue('CodexPetCompanion') } else { $null } }
finally { if ($null -ne $legacyKey) { $legacyKey.Dispose() } }
$oldExe = if ($oldCommand -match '^"([^"]+)"') { $Matches[1] } else { $null }

# Close only this helper, never the Codex app. Retain previous versions on disk.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CompanionInstallerWindows {
    public delegate bool Callback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool EnumWindows(Callback callback, IntPtr parameter);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
}
'@
foreach ($process in @(Get-Process -Name CodexPetCompanion -ErrorAction SilentlyContinue)) {
    if ($process.Path -notin @($oldExe, $sourceExe, $targetExe) -and -not $process.Path.StartsWith($installRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Close the other running companion before installing.' }
    $companionProcessId = $process.Id
    [void][CompanionInstallerWindows]::EnumWindows({ param($window, $parameter)
        [uint32]$windowProcessId = 0
        [void][CompanionInstallerWindows]::GetWindowThreadProcessId($window, [ref]$windowProcessId)
        if ($windowProcessId -eq $companionProcessId) {
            [void][CompanionInstallerWindows]::PostMessage($window, 0x10, [IntPtr]::Zero, [IntPtr]::Zero)
        }
        return $true
    }, [IntPtr]::Zero)
    if (-not $process.WaitForExit(30000)) { throw 'Companion is still closing. Try again in a moment.' }
}

New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($name in @('CodexPetCompanion.exe', 'LICENSE', 'README.md', 'install.cmd', 'install.ps1')) {
    $source = Join-Path $PSScriptRoot $name
    $target = Join-Path $destination $name
    if ((Test-Path -LiteralPath $source) -and ([IO.Path]::GetFullPath($source) -ne [IO.Path]::GetFullPath($target))) {
        Copy-Item -LiteralPath $source -Destination $target -Force
    }
}
# Task Scheduler provides logon/unlock triggers plus recovery if a launch was missed.
# InteractiveToken keeps the UI in this user's desktop without storing a password.
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $userSid = $identity.User.Value } finally { $identity.Dispose() }
$taskName = 'CodexPetCompanion-' + $userSid
$scheduler = New-Object -ComObject Schedule.Service
$scheduler.Connect()
$folder = $scheduler.GetFolder('\')
$definition = $scheduler.NewTask(0)
$definition.RegistrationInfo.Description = 'Start the Codex companion at sign-in/unlock; recover missing companion once per minute while Codex is open.'
$definition.Principal.UserId = $userSid
$definition.Principal.LogonType = 3
$definition.Principal.RunLevel = 0
$definition.Settings.Enabled = $true
$definition.Settings.StartWhenAvailable = $true
$definition.Settings.DisallowStartIfOnBatteries = $false
$definition.Settings.StopIfGoingOnBatteries = $false
$definition.Settings.ExecutionTimeLimit = 'PT0S'
$definition.Settings.MultipleInstances = 2
$logon = $definition.Triggers.Create(9)
$logon.UserId = $userSid
$logon.Delay = 'PT5S'
$unlock = $definition.Triggers.Create(11)
$unlock.UserId = $userSid
$unlock.StateChange = 8
$unlock.Delay = 'PT3S'
$recovery = $definition.Triggers.Create(1)
$recovery.StartBoundary = (Get-Date).AddSeconds(10).ToString('yyyy-MM-ddTHH:mm:ss')
$recovery.Repetition.Interval = 'PT1M'
$action = $definition.Actions.Create(0)
$action.Path = $targetExe
$action.Arguments = '--recover'
$action.WorkingDirectory = $destination
$registered = $folder.RegisterTaskDefinition($taskName, $definition, 6, $userSid, $null, 3)
if (-not $registered.Enabled -or $registered.Definition.Actions.Item(1).Path -ne $targetExe) {
    throw 'Scheduled startup could not be verified.'
}
# Remove only the legacy entry after the replacement has been verified.
$startupKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run', $true)
if ($null -ne $startupKey) {
    try { $startupKey.DeleteValue('CodexPetCompanion', $false) }
    finally { $startupKey.Dispose() }
}
$registered.Run($null) | Out-Null
Write-Output "Installed: $targetExe"
Write-Output "Scheduled startup registered and triggered: $taskName"
Write-Output 'The companion starts while Codex is open; missed launches recover within about one minute.'
