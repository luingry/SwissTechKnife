<#
    Release build: sample feed → solution → tests.
    pwsh -File scripts/build.ps1            (or powershell -File …)
    pwsh -File scripts/build.ps1 -Installer (also builds artifacts/installer/SwissTechKnife-<ver>-setup.exe)
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [switch]$Installer
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

& (Join-Path $PSScriptRoot 'pack-modules.ps1') -Configuration $Configuration
dotnet build (Join-Path $root 'SwissTechKnife.sln') -c $Configuration -nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
dotnet run --project (Join-Path $root 'tests/SwissTechKnife.Tests/SwissTechKnife.Tests.csproj') -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

if ($Installer) {
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found. Install it, then rerun with -Installer.' }
    & $iscc "/DAppVersion=$version" (Join-Path $root 'installer/SwissTechKnife.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE." }
}
Write-Host "SwissTechKnife $version $Configuration build passed." -ForegroundColor Green
