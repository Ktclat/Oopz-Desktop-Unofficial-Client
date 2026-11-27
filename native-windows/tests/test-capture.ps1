#requires -Version 5.1
param([Parameter(Mandatory)] [string] $AssemblyPath)
$ErrorActionPreference = 'Stop'
try {
    $assembly = (Resolve-Path -LiteralPath $AssemblyPath).ProviderPath
    $project = Join-Path $PSScriptRoot 'CaptureTests.csproj'
    $build = & msbuild $project /t:Build /nologo /v:minimal "/p:NativeAssemblyPath=$assembly"
    if ($LASTEXITCODE -ne 0) { throw ($build -join [Environment]::NewLine) }
    $result = & (Join-Path $PSScriptRoot 'bin/Release/CaptureTests.exe')
    if ($LASTEXITCODE -ne 0) { throw ($result -join [Environment]::NewLine) }
    Write-Output $result
} catch {
    if ($env:GITHUB_ACTIONS) {
        $reason = $_.Exception.GetBaseException().Message.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
        Write-Output "::error title=Native capture parser::$reason"
    }
    throw
}
