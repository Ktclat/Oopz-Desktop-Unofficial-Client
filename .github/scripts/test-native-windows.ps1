#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $ExePath)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class NativeWindowProbe {
    delegate bool Visitor(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(Visitor visitor, IntPtr state);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    public static IntPtr[] VisibleWindows(int processId) {
        var result = new List<IntPtr>();
        EnumWindows((window, state) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == processId && IsWindowVisible(window)) result.Add(window);
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }
}
'@

function Assert-NoBrowser($app) {
    $app.Refresh()
    if ($app.HasExited) { throw "The native client exited with code $($app.ExitCode)." }
    $browserModules = @($app.Modules | Where-Object {
        $_.ModuleName -in @('WebView2Loader.dll', 'EmbeddedBrowserWebView.dll', 'libcef.dll', 'chrome_elf.dll', 'mshtml.dll')
    })
    if ($browserModules) { throw "Browser modules loaded in the native client: $($browserModules.ModuleName -join ', ')" }

    $processes = @(Get-CimInstance Win32_Process -Filter "SessionId = $($app.SessionId)" -Property Name, ProcessId, ParentProcessId)
    $seen = [Collections.Generic.HashSet[uint32]]::new()
    $null = $seen.Add($app.Id)
    do {
        $added = $false
        foreach ($process in $processes) {
            if ($seen.Contains($process.ParentProcessId) -and $seen.Add($process.ProcessId)) {
                if ($process.Name -match '^(msedge(webview2)?|chrome|chromium|electron|CefSharp\.BrowserSubprocess|WebViewHost)\.exe$') {
                    throw "Native client launched a browser descendant: $($process.Name)"
                }
                $added = $true
            }
        }
    } while ($added)
}

$exe = (Resolve-Path -LiteralPath $ExePath).ProviderPath
$app = Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -PassThru
try {
    $buttonId = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'MicrophoneCheck')
    $button = $null
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $app.Refresh()
        if ($app.HasExited) { throw "Startup failed with exit code $($app.ExitCode)." }
        $main = $app.MainWindowHandle
        if ($main -ne [IntPtr]::Zero) {
            $root = [System.Windows.Automation.AutomationElement]::FromHandle($main)
            $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $buttonId)
            if ($button -and $button.Current.IsEnabled) { break }
        }
        Start-Sleep -Milliseconds 250
    } while ($timer.Elapsed.TotalSeconds -lt 30)
    if (-not $button -or -not $button.Current.IsEnabled -or $root.Current.FrameworkId -ne 'WPF') {
        throw 'No interactive WPF main window with an enabled microphone check button appeared.'
    }

    for ($i = 0; $i -lt 5; $i++) {
        Assert-NoBrowser $app
        Start-Sleep -Milliseconds 400
    }
    $app.Refresh()
    $private = [math]::Round($app.PrivateMemorySize64 / 1MB, 1)
    $resident = [math]::Round($app.WorkingSet64 / 1MB, 1)
    $sample = "Native main window open, before microphone check: $private MiB private bytes, $resident MiB working set (one host-process sample)."
    Write-Output $sample
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content $env:GITHUB_STEP_SUMMARY $sample }
    if ($env:GITHUB_ACTIONS) { Write-Output "::notice title=Native process memory::$sample" }

    $original = [NativeWindowProbe]::VisibleWindows($app.Id)
    $invoke = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
    $timer.Restart()
    $dialog = $null
    do {
        foreach ($handle in [NativeWindowProbe]::VisibleWindows($app.Id)) {
            if ($handle -in $original) { continue }
            $element = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
            if ($element.Current.FrameworkId -eq 'WPF' -and
                $element.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
                $dialog = $element
                break
            }
        }
        if (-not $dialog) { Start-Sleep -Milliseconds 200 }
    } while (-not $dialog -and $timer.Elapsed.TotalSeconds -lt 10)
    if (-not $dialog) { throw 'Invoking microphone check did not open a native WPF dialog.' }
    Assert-NoBrowser $app
    $dialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    $timer.Restart()
    while (-not $root.Current.IsEnabled -and $timer.Elapsed.TotalSeconds -lt 5) {
        Start-Sleep -Milliseconds 100
    }
    & (Join-Path $PSScriptRoot 'test-native-adapter.ps1') -App $app
    Assert-NoBrowser $app
    $root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    if (-not $app.WaitForExit(10000) -or $app.ExitCode -ne 0) { throw 'The native client did not close cleanly.' }
    Write-Output 'Main window, native microphone dialog and clean exit passed. No browser modules or descendant processes were observed.'
} finally {
    if (-not $app.HasExited) { $app.Kill() }
    $app.Dispose()
}
