#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ExePath,
    [switch] $SkipIfNoDesktop
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Run this smoke test on Windows.' }
$exe = (Resolve-Path -LiteralPath $ExePath).ProviderPath
$sessionId = [Diagnostics.Process]::GetCurrentProcess().SessionId
$explorer = Get-Process -Name explorer -ErrorAction SilentlyContinue |
    Where-Object SessionId -eq $sessionId | Select-Object -First 1
if ($sessionId -eq 0 -or -not [Environment]::UserInteractive -or -not $explorer) {
    $message = 'Tray startup smoke test skipped. An interactive Windows session with Explorer is required.'
    if (-not $SkipIfNoDesktop) { throw $message }
    Write-Warning $message
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $message }
    return
}

$profileDirectory = Join-Path ([IO.Path]::GetTempPath()) ("oopz-webview-test-" + [guid]::NewGuid())
function Start-Oopz([switch] $TrayOnly) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $exe
    $info.WorkingDirectory = Split-Path -Parent $exe
    $info.UseShellExecute = $false
    # A unique profile avoids sharing a WebView2 browser with another app.
    $info.Environment['WEBVIEW2_USER_DATA_FOLDER'] = $profileDirectory
    $info.Environment['WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS'] = '--proxy-server=http://127.0.0.1:9'
    if ($TrayOnly) { $info.ArgumentList.Add('--tray-only') }
    [Diagnostics.Process]::Start($info)
}

function Test-WebViewDescendant([int] $RootId) {
    $processes = Get-CimInstance -ClassName Win32_Process -Filter "SessionId = $sessionId" -Property ProcessId, ParentProcessId, Name
    $tree = [Collections.Generic.HashSet[uint32]]::new()
    $null = $tree.Add($RootId)
    do {
        $added = $false
        foreach ($process in $processes) {
            if ($tree.Contains($process.ParentProcessId) -and $tree.Add($process.ProcessId)) {
                if ($process.Name -ieq 'msedgewebview2.exe') { return $true }
                $added = $true
            }
        }
    } while ($added)
    return $false
}

# Exercise the command bound to the actual native menu without relying on screen coordinates.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class OopzMenu {
    [DllImport("user32")] static extern IntPtr GetMenu(IntPtr window);
    [DllImport("user32")] static extern IntPtr GetSubMenu(IntPtr menu, int position);
    [DllImport("user32")] static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32")] static extern uint GetMenuItemID(IntPtr menu, int position);
    [DllImport("user32", CharSet=CharSet.Unicode)] static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int size, uint flags);
    [DllImport("user32")] static extern bool PostMessageW(IntPtr window, uint message, UIntPtr command, IntPtr data);
    public static void UnloadPage(IntPtr window) {
        var menu = GetSubMenu(GetMenu(window), 0);
        for (int i = 0; i < GetMenuItemCount(menu); i++) {
            var name = new StringBuilder(128);
            GetMenuString(menu, (uint)i, name, name.Capacity, 0x400); // MF_BYPOSITION
            if (name.ToString() == "断开会话并卸载网页") {
                if (!PostMessageW(window, 0x111, (UIntPtr)GetMenuItemID(menu,i), IntPtr.Zero))
                    throw new InvalidOperationException("Could not invoke the Oopz menu command.");
                return;
            }
        }
        throw new InvalidOperationException("Unload command was missing from the main window menu.");
    }
}
'@

function Wait-ForActivation($primary, $second) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $primary.Refresh()
        if ($primary.HasExited) { throw 'The original tray instance exited during activation.' }
        if ($second.HasExited -and $second.ExitCode -ne 0) { throw "Second launch failed with code $($second.ExitCode)." }
        $ready = $second.HasExited -and $primary.MainWindowHandle -ne [IntPtr]::Zero -and (Test-WebViewDescendant $primary.Id)
        if ($ready) { break }
        Start-Sleep -Milliseconds 400
    } while ($clock.Elapsed.TotalSeconds -lt 60)
    if (-not $ready) { throw 'Timed out waiting for single-instance activation, a visible main window and a WebView2 descendant.' }
}

$primary = $null
$second = $null
try {
    $primary = Start-Oopz -TrayOnly
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $primary.Refresh()
        if ($primary.HasExited) { throw "Tray instance exited early with code $($primary.ExitCode)." }
        if ($primary.MainWindowHandle -ne [IntPtr]::Zero -or (Test-WebViewDescendant $primary.Id)) {
            throw 'Tray-only startup unexpectedly created a visible main window or WebView2 process.'
        }
        Start-Sleep -Milliseconds 250
    } while ($clock.Elapsed.TotalSeconds -lt 8)

    $primary.Refresh()
    $privateMiB = [math]::Round($primary.PrivateMemorySize64 / 1MB, 1)
    $result = "Tray-only startup after 8 seconds: no visible main window, no sampled WebView2 descendants, Oopz private memory $privateMiB MiB."
    Write-Output $result
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $result }
    $second = Start-Oopz
    Wait-ForActivation $primary $second
    $second.Dispose()
    $second = $null
    [OopzMenu]::UnloadPage($primary.MainWindowHandle)
    $clock.Restart()
    do {
        $primary.Refresh()
        if ($primary.HasExited) { throw 'Unloading the webpage exited the tray process.' }
        $idle = $primary.MainWindowHandle -eq [IntPtr]::Zero -and -not (Test-WebViewDescendant $primary.Id)
        if ($idle) { break }
        Start-Sleep -Milliseconds 400
    } while ($clock.Elapsed.TotalSeconds -lt 60)
    if (-not $idle) { throw 'Unloading did not release the window and private-profile WebView2 tree.' }
    $second = Start-Oopz
    Wait-ForActivation $primary $second
    $result = 'Passed tray-only startup, activation, menu-driven unload and reactivation of the same Oopz process.'
    Write-Output $result
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $result }
} finally {
    foreach ($process in @($second, $primary)) {
        if ($null -eq $process) { continue }
        try {
            if (-not $process.HasExited) {
                $process.Kill($true)
                $null = $process.WaitForExit(5000)
            }
        } catch {
            if (-not $process.HasExited) { Write-Warning "Could not stop test process $($process.Id): $_" }
        } finally {
            $process.Dispose()
        }
    }
    if (Test-Path -LiteralPath $profileDirectory) {
        Remove-Item -LiteralPath $profileDirectory -Recurse -Force -ErrorAction Continue
    }
}
