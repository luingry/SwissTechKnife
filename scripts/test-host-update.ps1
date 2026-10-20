<#
    End-to-end test of the host self-update against a local feed, without UAC and without touching the real install:
      1. builds two per-user test installers (/DTestInstall) for an "old" and a "new" version,
      2. installs the old one into %LOCALAPPDATA%\SwissTechKnife-Test,
      3. writes artifacts/test-feed/catalog.json with a "host" entry for the new one (scripts/publish-host.ps1),
      4. starts the old app on an isolated profile (STK_HOME) and clicks Configuracoes > Atualizar > Atualizar
         through UI Automation,
      5. waits for the installer to replace the app and relaunch it, then checks the version, the outcome record
         and the update log.
    powershell -File scripts/test-host-update.ps1          (-Keep leaves the test install and profile for inspection)
#>
[CmdletBinding()]
param(
    [string]$OldVersion = '0.3.90',
    [string]$NewVersion = '0.3.91',
    [switch]$Keep
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$installDir = Join-Path $env:LOCALAPPDATA 'SwissTechKnife-Test'
$exe = Join-Path $installDir 'SwissTechKnife.exe'
$feed = Join-Path $root 'artifacts/test-feed'
$home_ = Join-Path $root 'artifacts/test-home'
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found.' }

function Step([string]$text) { Write-Host "-- $text" -ForegroundColor Cyan }

function Build-TestInstaller([string]$version) {
    dotnet build (Join-Path $root 'src/SwissTechKnife/SwissTechKnife.csproj') -c Release -nologo -v q "-p:Version=$version" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build $version failed." }
    & $iscc /Q /DTestInstall "/DAppVersion=$version" (Join-Path $root 'installer/SwissTechKnife.iss') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "ISCC $version failed with exit code $LASTEXITCODE." }
    Join-Path $root "artifacts/installer/SwissTechKnife-$version-setup-test.exe"
}

function Stop-TestApp {
    Get-Process SwissTechKnife -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($installDir, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { $_.Kill(); $_.WaitForExit(5000) | Out-Null }
}

function Remove-TestInstall {
    Stop-TestApp
    $uninstaller = Join-Path $installDir 'unins000.exe'
    if (Test-Path $uninstaller) {
        $p = Start-Process $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru -Wait
        Write-Host "Uninstalled the test install (exit code $($p.ExitCode))."
    }
}

function Wait-Until([scriptblock]$condition, [int]$seconds, [string]$what) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $value = & $condition
        if ($value) { return $value }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out after $seconds s waiting for $what."
}

# UI Automation: find a control by AutomationId (x:Name) or Name inside the app window and press it.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
function Find-Control([int]$processId, [string]$automationId, [string]$name) {
    $ae = [Windows.Automation.AutomationElement]
    $window = $ae::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,
        (New-Object Windows.Automation.PropertyCondition($ae::ProcessIdProperty, $processId)))
    if (-not $window) { return $null }
    $condition = if ($automationId) { New-Object Windows.Automation.PropertyCondition($ae::AutomationIdProperty, $automationId) }
                 else { New-Object Windows.Automation.PropertyCondition($ae::NameProperty, $name) }
    $element = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if ($element -and $element.Current.IsEnabled -and -not $element.Current.IsOffscreen) { return $element }
    $null
}
function Invoke-Control($element) {
    $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}

try {
    Step "Building test installers $OldVersion and $NewVersion"
    $oldSetup = Build-TestInstaller $OldVersion
    $newSetup = Build-TestInstaller $NewVersion

    Step 'Installing the old version'
    Remove-TestInstall
    $p = Start-Process $oldSetup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru -Wait
    if ($p.ExitCode -ne 0) { throw "Old installer failed with exit code $($p.ExitCode)." }
    $installed = (Get-Item $exe).VersionInfo.ProductVersion
    if (-not $installed.StartsWith($OldVersion)) { throw "Expected $OldVersion installed, found $installed." }

    Step 'Writing the test feed'
    if (Test-Path $feed) { Remove-Item -Recurse -Force $feed }
    if (Test-Path $home_) { Remove-Item -Recurse -Force $home_ }
    New-Item -ItemType Directory -Force $feed | Out-Null
    $setupName = Split-Path $newSetup -Leaf
    Copy-Item $newSetup (Join-Path $feed $setupName)
    [IO.File]::WriteAllText((Join-Path $feed 'catalog.json'), "{`n  `"schema`": 1,`n  `"modules`": []`n}`n", (New-Object Text.UTF8Encoding $false))
    & (Join-Path $PSScriptRoot 'publish-host.ps1') -SkipBuild -SkipRelease -Catalog (Join-Path $feed 'catalog.json') `
        -Version $NewVersion -InstallerPath (Join-Path $feed $setupName) -InstallerUrl $setupName `
        -Notes 'Teste de atualizacao automatica.', 'Segunda nota de teste.'

    Step 'Starting the old app on an isolated profile'
    $env:STK_HOME = $home_
    $env:STK_CATALOG = Join-Path $feed 'catalog.json'
    $old = Start-Process $exe -PassThru

    $configuracoes = 'Configura' + [char]0xE7 + [char]0xF5 + 'es'
    $nav = Wait-Until { Find-Control $old.Id $null $configuracoes } 30 'the Configuracoes button'
    Invoke-Control $nav
    $notice = Wait-Until { Find-Control $old.Id 'HostUpdateButton' $null } 30 'the update notice'
    Write-Host 'Update notice shown.'
    Invoke-Control $notice
    $confirm = Wait-Until { Find-Control $old.Id 'ConfirmButton' $null } 10 'the confirmation popup'
    Write-Host 'Confirmation popup shown.'
    Invoke-Control $confirm

    Step 'Waiting for the update'
    if (-not $old.WaitForExit(120000)) { throw 'The old app did not exit for the update.' }
    Write-Host "Old app exited (code $($old.ExitCode))."
    $new = Wait-Until {
        Get-Process SwissTechKnife -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and $_.Path.StartsWith($installDir, [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1
    } 180 'the relaunched app'
    $version = (Get-Item $exe).VersionInfo.ProductVersion
    if (-not $version.StartsWith($NewVersion)) { throw "Expected $NewVersion after the update, found $version." }
    Write-Host "Relaunched: $version (pid $($new.Id))."

    $record = Join-Path $home_ 'downloads/host-update.json'
    Wait-Until { -not (Test-Path $record) } 30 'the success report (host-update.json cleared)' | Out-Null
    $log = Get-ChildItem (Join-Path $home_ 'logs') -Filter *.log | Sort-Object LastWriteTime | Select-Object -Last 1
    $updated = Wait-Until { Select-String -Path $log.FullName -Pattern "Updated $OldVersion" -SimpleMatch -Quiet } 15 'the "Updated" log line'
    $setupLog = Get-ChildItem (Join-Path $home_ 'downloads') -Filter 'host-update-*.log' | Select-Object -First 1
    if ($setupLog) {
        Select-String -Path $setupLog.FullName -Pattern 'released its mutex', 'Relaunched', 'Exit code' | ForEach-Object { Write-Host "   setup: $($_.Line.Trim())" }
    }
    if (Get-ChildItem (Join-Path $home_ 'downloads') -Filter '*.exe') { throw 'The installer should be deleted after a successful update.' }
    Write-Host "PASS: $OldVersion -> $NewVersion updated, relaunched and reported." -ForegroundColor Green
}
finally {
    Remove-Item Env:STK_HOME -ErrorAction SilentlyContinue
    Remove-Item Env:STK_CATALOG -ErrorAction SilentlyContinue
    if (-not $Keep) {
        Remove-TestInstall
        Remove-Item -Recurse -Force $home_, $feed -ErrorAction SilentlyContinue
    }
    # The test builds stamped another version into bin/Release: rebuild with the real one.
    dotnet build (Join-Path $root 'src/SwissTechKnife/SwissTechKnife.csproj') -c Release -nologo -v q | Out-Null
}
