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
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$oldCommand = Get-ItemPropertyValue -LiteralPath $runKey -Name CodexPetCompanion -ErrorAction SilentlyContinue
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
    if ($process.Path -notin @($oldExe, $sourceExe, $targetExe)) { throw 'Close the other running companion before installing.' }
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
$command = '"' + $targetExe + '" --startup'
$startupKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
try { $startupKey.SetValue('CodexPetCompanion', $command, [Microsoft.Win32.RegistryValueKind]::String) }
finally { $startupKey.Dispose() }
if ((Get-ItemPropertyValue -LiteralPath $runKey -Name CodexPetCompanion) -ne $command -or -not (Test-Path -LiteralPath $targetExe)) {
    throw 'Startup registration could not be verified.'
}
Start-Process -FilePath $targetExe -WorkingDirectory $destination -WindowStyle Hidden
Write-Output "Installed: $targetExe"
Write-Output 'Windows sign-in startup registered. The companion waits in the tray until Codex opens.'
