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

# The MSVC executable uses the system Evergreen WebView2 Runtime.
$clientKey = 'Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
function Get-RuntimeVersion {
    foreach ($key in @("HKLM:\SOFTWARE\WOW6432Node\$clientKey", "HKCU:\Software\$clientKey")) {
        $value = Get-ItemPropertyValue -LiteralPath $key -Name pv -ErrorAction SilentlyContinue
        if ($value -and [version]$value -gt [version]'0.0.0.0') { return $value }
    }
}
$runtimeVersion = Get-RuntimeVersion
if (-not $runtimeVersion -and $env:RUNNER_ENVIRONMENT -eq 'github-hosted') {
    $installer = Join-Path $env:RUNNER_TEMP 'MicrosoftEdgeWebview2Setup.exe'
    Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $installer
    $signature = Get-AuthenticodeSignature -FilePath $installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        throw 'WebView2 installer signature verification failed.'
    }
    $setup = Start-Process $installer -ArgumentList '/silent', '/install' -Wait -PassThru
    $runtimeVersion = Get-RuntimeVersion
    if (-not $runtimeVersion) { throw "WebView2 runtime setup failed with code $($setup.ExitCode)." }
}
if (-not $runtimeVersion) { throw 'This test needs the Microsoft Edge WebView2 Runtime.' }

$profileDirectory = Join-Path ([IO.Path]::GetTempPath()) ("oopz-webview-test-" + [guid]::NewGuid())
function Start-Oopz([switch] $TrayOnly) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $exe
    $info.WorkingDirectory = Split-Path -Parent $exe
    $info.UseShellExecute = $false
    $info.RedirectStandardError = $true
    # A unique profile avoids sharing a WebView2 browser with another app.
    $info.Environment['WEBVIEW2_USER_DATA_FOLDER'] = $profileDirectory
    $info.Environment['WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS'] = '--proxy-server=http://127.0.0.1:9'
    if ($TrayOnly) { $info.ArgumentList.Add('--tray-only') }
    $child = [Diagnostics.Process]::Start($info)
    if ($TrayOnly) { $script:stderrRead = $child.StandardError.ReadToEndAsync() }
    return $child
}

function Test-WebViewDescendant([int] $RootId) {
    $processes = Get-CimInstance -ClassName Win32_Process -Filter "SessionId = $sessionId" -Property ProcessId, ParentProcessId, Name, CommandLine
    # WebView2 can also create its browser process through an OS broker.
    foreach ($process in $processes) {
        if ($process.Name -ieq 'msedgewebview2.exe' -and $process.CommandLine -like "*$profileDirectory*") { return $true }
    }
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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class OopzMenu {
    private delegate bool VisitWindow(IntPtr window, IntPtr state);
    [DllImport("user32")] static extern bool EnumWindows(VisitWindow visitor, IntPtr state);
    [DllImport("user32")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder text, int size);
    [DllImport("user32", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
    [DllImport("user32")] static extern IntPtr GetMenu(IntPtr window);
    [DllImport("user32")] static extern IntPtr GetSubMenu(IntPtr menu, int position);
    [DllImport("user32")] static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32")] static extern uint GetMenuItemID(IntPtr menu, int position);
    [DllImport("user32", CharSet=CharSet.Unicode)] static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int size, uint flags);
    [DllImport("user32")] static extern bool PostMessageW(IntPtr window, uint message, UIntPtr command, IntPtr data);
    private static List<IntPtr> Windows(int processId) {
        var result = new List<IntPtr>();
        EnumWindows((window, state) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == processId) result.Add(window);
            return true;
        }, IntPtr.Zero);
        return result;
    }
    private static string ClassName(IntPtr window) {
        var text = new StringBuilder(128);
        GetClassName(window, text, text.Capacity);
        return text.ToString();
    }
    public static IntPtr FindWindow(int processId, string className, bool visibleOnly) {
        // Tauri also creates a zero-size single-instance message window.
        foreach (var window in Windows(processId))
            if ((!visibleOnly || IsWindowVisible(window)) && ClassName(window) == className)
                return window;
        return IntPtr.Zero;
    }
    public static string Describe(int processId) {
        var result = new List<string>();
        foreach (var window in Windows(processId)) {
            var title = new StringBuilder(128);
            GetWindowText(window, title, title.Capacity);
            result.Add($"HWND={window} '{ClassName(window)}' '{title}' visible={IsWindowVisible(window)} menu={GetMenu(window)}");
        }
        return string.Join(" | ", result);
    }
    public static uint UnloadCommand(IntPtr window) {
        var bar = GetMenu(window);
        if (bar == IntPtr.Zero) return 0;
        var menu = GetSubMenu(bar, 0);
        for (int i = 0; i < GetMenuItemCount(menu); i++) {
            var name = new StringBuilder(128);
            GetMenuString(menu, (uint)i, name, name.Capacity, 0x400); // MF_BYPOSITION
            if (name.ToString() == "断开会话并卸载网页")
                return GetMenuItemID(menu, i);
        }
        return 0;
    }
    public static void UnloadPage(IntPtr window) {
        uint command = UnloadCommand(window);
        if (command == 0 || !PostMessageW(window, 0x111, (UIntPtr)command, IntPtr.Zero))
            throw new InvalidOperationException("Could not invoke the Oopz menu command.");
    }
    public static void CloseToTray(IntPtr window) {
        if (!PostMessageW(window, 0x0010, UIntPtr.Zero, IntPtr.Zero)) // WM_CLOSE
            throw new InvalidOperationException("Could not close the Oopz window.");
    }
}
'@

function Wait-ForActivation($primary, $second, [string] $stage) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $primary.Refresh()
        if ($primary.HasExited) { throw "The original tray instance exited during $stage." }
        if ($second.HasExited -and $second.ExitCode -ne 0) { throw "Second launch failed with code $($second.ExitCode)." }
        $window = [OopzMenu]::FindWindow($primary.Id, 'Tauri Window', $true)
        $command = if ($window -ne [IntPtr]::Zero) { [OopzMenu]::UnloadCommand($window) } else { 0 }
        if ($second.HasExited -and $command -ne 0 -and (Test-WebViewDescendant $primary.Id)) { return $window }
        if (([OopzMenu]::FindWindow($primary.Id, '#32770', $true)) -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 400
    } while ($clock.Elapsed.TotalSeconds -lt 60)
    $browser = Test-WebViewDescendant $primary.Id
    throw "Failed $stage. SecondExited=$($second.HasExited), MainWindow=$window, UnloadCommand=$command, Browser=$browser, Runtime=$runtimeVersion. $([OopzMenu]::Describe($primary.Id))"
}

$primary = $null
$second = $null
try {
    $primary = Start-Oopz -TrayOnly
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $primary.Refresh()
        if ($primary.HasExited) { throw "Tray instance exited early with code $($primary.ExitCode)." }
        $hasBrowser = Test-WebViewDescendant $primary.Id
        $window = [OopzMenu]::FindWindow($primary.Id, 'Tauri Window', $false)
        $dialog = [OopzMenu]::FindWindow($primary.Id, '#32770', $true)
        if ($window -ne [IntPtr]::Zero -or $dialog -ne [IntPtr]::Zero -or $hasBrowser) {
            throw "Tray-only startup unexpectedly created a window or runtime. WebView2=$hasBrowser. $([OopzMenu]::Describe($primary.Id))"
        }
        Start-Sleep -Milliseconds 250
    } while ($clock.Elapsed.TotalSeconds -lt 8)

    $primary.Refresh()
    $privateMiB = [math]::Round($primary.PrivateMemorySize64 / 1MB, 1)
    $result = "Tray-only startup after 8 seconds: no visible main window, no sampled WebView2 descendants, Oopz private memory $privateMiB MiB."
    Write-Output $result
    if ($env:GITHUB_ACTIONS) { Write-Output "::notice title=Tray check::$result" }
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $result }
    $second = Start-Oopz
    $window = Wait-ForActivation $primary $second 'first activation'
    $second.Dispose()
    $second = $null
    [OopzMenu]::CloseToTray($window)
    $clock.Restart()
    do {
        $primary.Refresh()
        if ($primary.HasExited) { throw 'Closing the window exited the tray process.' }
        $visible = [OopzMenu]::FindWindow($primary.Id, 'Tauri Window', $true)
        if ($visible -eq [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 250
    } while ($clock.Elapsed.TotalSeconds -lt 15)
    $retained = [OopzMenu]::FindWindow($primary.Id, 'Tauri Window', $false)
    if ($visible -ne [IntPtr]::Zero -or $retained -ne $window -or -not (Test-WebViewDescendant $primary.Id)) {
        throw "Closing did not keep the same hidden window and WebView2 session. $([OopzMenu]::Describe($primary.Id))"
    }
    $second = Start-Oopz
    $restored = Wait-ForActivation $primary $second 'restore after close-to-tray'
    if ($restored -ne $window) { throw 'Restoring after close-to-tray replaced the original window.' }
    $second.Dispose()
    $second = $null
    [OopzMenu]::UnloadPage($window)
    $clock.Restart()
    do {
        $primary.Refresh()
        if ($primary.HasExited) { throw 'Unloading the webpage exited the tray process.' }
        $idle = ([OopzMenu]::FindWindow($primary.Id, 'Tauri Window', $false)) -eq [IntPtr]::Zero -and -not (Test-WebViewDescendant $primary.Id)
        if ($idle) { break }
        Start-Sleep -Milliseconds 400
    } while ($clock.Elapsed.TotalSeconds -lt 60)
    if (-not $idle) { throw "Unloading did not release the window and private-profile WebView2 tree. $([OopzMenu]::Describe($primary.Id))" }
    $primary.Refresh()
    $privateMiB = [math]::Round($primary.PrivateMemorySize64 / 1MB, 1)
    $result = "After menu-driven unload: no main window or sampled WebView2 processes, Oopz private memory $privateMiB MiB."
    Write-Output $result
    if ($env:GITHUB_ACTIONS) { Write-Output "::notice title=Tray check::$result" }
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $result }
    $second = Start-Oopz
    $null = Wait-ForActivation $primary $second 'reactivation after unload'
    $result = 'Passed tray-only startup, activation, close-to-tray with the same window restored, menu-driven unload and reactivation of the same Oopz process.'
    Write-Output $result
    if ($env:GITHUB_ACTIONS) { Write-Output "::notice title=Tray check::$result" }
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $result }
} catch {
    if ($env:GITHUB_ACTIONS) {
        $detail = ("{0} (line {1})" -f $_.Exception.Message, $_.InvocationInfo.ScriptLineNumber)
        $detail = $detail.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
        Write-Output "::error title=Tray smoke test::$detail"
    }
    throw
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
    if ($null -ne $script:stderrRead -and $script:stderrRead.IsCompletedSuccessfully) {
        $diagnostic = $script:stderrRead.Result.Trim()
        if ($diagnostic) {
            Write-Output $diagnostic
            if ($env:GITHUB_ACTIONS) {
                $snippet = $diagnostic.Substring(0, [math]::Min(600, $diagnostic.Length))
                Write-Output ("::notice title=Oopz process diagnostic::" + $snippet.Replace('%','%25').Replace("`n",'%0A').Replace("`r",'%0D'))
            }
        }
    }
    if (Test-Path -LiteralPath $profileDirectory) {
        Remove-Item -LiteralPath $profileDirectory -Recurse -Force -ErrorAction Continue
    }
}
