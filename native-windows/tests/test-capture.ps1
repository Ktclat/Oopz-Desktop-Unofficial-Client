#requires -Version 5.1
param([Parameter(Mandatory)] [string] $AssemblyPath)
$ErrorActionPreference = 'Stop'
$assembly = (Resolve-Path -LiteralPath $AssemblyPath).ProviderPath
[void][Reflection.Assembly]::LoadFrom($assembly)
Add-Type -Path (Join-Path $PSScriptRoot 'CaptureTests.cs') -ReferencedAssemblies @(
    $assembly, 'System.dll', 'System.Core.dll', 'System.Web.Extensions.dll'
)
[CaptureTests]::Run()
Write-Output 'Oopz capture parser tests passed.'
