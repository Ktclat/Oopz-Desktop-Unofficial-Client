#requires -Version 5.1
# Invoked from the native desktop smoke test, which owns the WPF process and Win32 probe.
[CmdletBinding()]
param([Parameter(Mandatory)] $App)
$ErrorActionPreference = 'Stop'

function Wait-For($description, [scriptblock] $probe) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $result = & $probe
        if ($result) { return $result }
        if ($App.HasExited) { throw "Client exited while waiting for $description." }
        Start-Sleep -Milliseconds 150
    } while ($timer.Elapsed.TotalSeconds -lt 15)
    throw "Timed out waiting for $description."
}

function Find-Control([string] $id) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    foreach ($handle in [NativeWindowProbe]::VisibleWindows($App.Id)) {
        $window = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
        $control = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($control -and $control.Current.IsEnabled) { return $control }
    }
}

function Invoke-Button([string] $id) {
    $button = Wait-For $id { Find-Control $id }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Set-ControlText([string] $id, [string] $value) {
    $box = Wait-For $id { Find-Control $id }
    $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}

function Wait-ForText([string] $id, [string] $text) {
    $null = Wait-For "$id containing '$text'" {
        $control = Find-Control $id
        if (-not $control) { return $false }
        $children = $control.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($child in $children) {
            if ($child.Current.Name -like "*$text*") { return $true }
        }
        return $false
    }
}

$dir = Join-Path ([IO.Path]::GetTempPath()) ("oopz-native-test-" + [Guid]::NewGuid())
$null = New-Item -ItemType Directory -Path $dir
$fixture = $null
try {
    $fixtureScript = Join-Path $PSScriptRoot 'native-adapter-fixture.py'
    $fixture = Start-Process -FilePath 'python' -ArgumentList @('-u', "`"$fixtureScript`"", "`"$dir`"") -NoNewWindow -PassThru
    $file = Join-Path $dir 'ready.json'
    $null = Wait-For 'loopback fixture' {
        if ($fixture.HasExited) { throw "Fixture exited with code $($fixture.ExitCode)." }
        Test-Path -LiteralPath $file
    }
    $port = (Get-Content $file -Raw | ConvertFrom-Json).port
    $button = Wait-For 'connection button' { Find-Control 'ConnectButton' }
    $initialName = $button.Current.Name
    Invoke-Button 'ConnectButton'
    Set-ControlText 'AdapterPort' ([string]$port)
    Set-ControlText 'AdapterToken' 'native-smoke-only'
    Invoke-Button 'ConfirmConnect'

    Wait-ForText 'Channels' 'Native fixture channel'
    Wait-ForText 'Timeline' 'Native fixture cached'
    $null = New-Item -ItemType File -Path (Join-Path $dir 'push-event')
    Wait-ForText 'Timeline' 'Native fixture incoming'

    $outgoing = 'Native fixture outgoing'
    Set-ControlText 'Composer' $outgoing
    Invoke-Button 'SendMessage'
    $file = Join-Path $dir 'sent.json'
    $null = Wait-For 'explicit message delivery to fixture' { Test-Path -LiteralPath $file }
    $sent = Get-Content $file -Raw | ConvertFrom-Json
    if ($sent.group_id -ne 100 -or $sent.message -cne $outgoing -or $sent.auto_escape -ne $true) {
        throw 'Message payload did not match the selected channel and plain text.'
    }
    Invoke-Button 'ConnectButton'
    $null = Wait-For 'disconnection' {
        $button = Find-Control 'ConnectButton'
        $button -and $button.Current.Name -eq $initialName
    }
    $message = 'Native UI connected to the loopback fixture, showed its channel, cached and incoming text, sent one message and disconnected.'
    Write-Output $message
    if ($env:GITHUB_ACTIONS) { Write-Output "::notice title=Native text integration::$message" }
} finally {
    if ($fixture) {
        if (-not $fixture.HasExited) { $fixture.Kill(); $null = $fixture.WaitForExit(3000) }
        $fixture.Dispose()
    }
    Remove-Item -Recurse -Force -LiteralPath $dir
}
