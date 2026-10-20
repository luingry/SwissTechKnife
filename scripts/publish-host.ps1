<#
    Publishes a SwissTechKnife release for self-update:
      1. builds the installer (scripts/build.ps1 -Installer),
      2. creates (or updates) the GitHub release v<version> with the installer attached,
      3. writes the "host" entry of catalog/catalog.json: version, installer URL, sha256, size, releaseNotes,
      4. deletes older GitHub releases (and their tags) so only the newest -KeepReleases (default 3) remain.
    Commit and push catalog.json afterwards: running apps see the update at their next check.

    powershell -File scripts/publish-host.ps1 -Notes "Primeira nota.", "Segunda nota."
    powershell -File scripts/publish-host.ps1 -SkipRelease            (catalog only, e.g. release already created)

    Test feeds (scripts/test-host-update.ps1) use -SkipBuild -InstallerPath -InstallerUrl -Catalog -SkipRelease.
#>
[CmdletBinding()]
param(
    # User-facing changes in English, one sentence each. Required unless the catalog already has notes for this version.
    [string[]]$Notes,
    [string]$Repo = 'luingry/SwissTechKnife',
    [string]$Catalog,
    [string]$Version,
    [string]$Date = (Get-Date -Format 'yyyy-MM-dd'),
    [switch]$SkipBuild,
    [switch]$SkipRelease,
    [string]$InstallerPath,
    # Defaults to the GitHub release asset URL; a bare file name is resolved relative to catalog.json.
    [string]$InstallerUrl,
    # Host releases (vX.Y.Z) kept on GitHub after publishing; older ones are deleted with their tags.
    [ValidateRange(1, 100)]
    [int]$KeepReleases = 3
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Catalog) { $Catalog = Join-Path $root 'catalog/catalog.json' }
if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
    $Version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must be major.minor.patch, got '$Version'." }

# Release notes: new ones replace this version's entry; older entries are kept
$catalogText = [IO.File]::ReadAllText($Catalog, [Text.Encoding]::UTF8)
$current = $catalogText | ConvertFrom-Json
$history = @()
if ($current.host -and $current.host.releaseNotes) { $history = @($current.host.releaseNotes | Where-Object { $_.version -ne $Version }) }
$existing = if ($current.host -and $current.host.releaseNotes) { $current.host.releaseNotes | Where-Object { $_.version -eq $Version } | Select-Object -First 1 }
if ($Notes) {
    $entry = [pscustomobject]@{ version = $Version; date = $Date; notes = @($Notes) }
} elseif ($existing) {
    $entry = $existing
} else {
    throw "No release notes for $Version. Pass -Notes `"...`", `"...`" (English, one sentence each)."
}
$releaseNotes = @(@($entry) + $history | Sort-Object { [version]$_.version } -Descending)

# Installer
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Installer
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) { throw 'Build failed.' }
}
if (-not $InstallerPath) { $InstallerPath = Join-Path $root "artifacts/installer/SwissTechKnife-$Version-setup.exe" }
if (-not (Test-Path $InstallerPath)) { throw "Installer not found: $InstallerPath" }
$sha256 = (Get-FileHash $InstallerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item $InstallerPath).Length
$assetName = Split-Path $InstallerPath -Leaf
if (-not $InstallerUrl) { $InstallerUrl = "https://github.com/$Repo/releases/download/v$Version/$assetName" }

# GitHub release
if (-not $SkipRelease) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI (gh) not found. Install it or pass -SkipRelease.' }
    # Windows PowerShell 5.1 turns native stderr into terminating errors under ErrorActionPreference=Stop.
    $ErrorActionPreference = 'Continue'
    & gh repo view $Repo --json name 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "GitHub repository $Repo not found (or gh is not signed in). Create it first: gh repo create $Repo --private --source . --push" }
    $body = ($entry.notes | ForEach-Object { "- $_" }) -join "`n"
    & gh release view "v$Version" --repo $Repo 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        & gh release upload "v$Version" $InstallerPath --repo $Repo --clobber
        if ($LASTEXITCODE -ne 0) { throw "gh release upload failed with exit code $LASTEXITCODE." }
    } else {
        & gh release create "v$Version" $InstallerPath --repo $Repo --title "SwissTechKnife $Version" --notes $body
        if ($LASTEXITCODE -ne 0) { throw "gh release create failed with exit code $LASTEXITCODE." }
    }
    $ErrorActionPreference = 'Stop'
    # The catalog must never point at a file that does not match: check what GitHub now serves.
    $check = Join-Path ([IO.Path]::GetTempPath()) ("stk-release-" + [guid]::NewGuid().ToString('N') + '.exe')
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        for ($attempt = 1; ; $attempt++) {
            try { Invoke-WebRequest $InstallerUrl -OutFile $check -UseBasicParsing; break }
            catch { if ($attempt -ge 5) { throw }; Start-Sleep -Seconds (3 * $attempt) }
        }
        $served = (Get-FileHash $check -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($served -ne $sha256) { throw "Release asset hash $served does not match the local installer $sha256." }
    } finally { Remove-Item $check -ErrorAction SilentlyContinue }

    # Keep only the newest $KeepReleases host releases; the catalog only ever points at the latest one.
    $ErrorActionPreference = 'Continue'
    $tags = @(& gh release list --repo $Repo --limit 200 --json tagName --jq '.[].tagName' 2>$null)
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'Could not list releases; old releases were not pruned.'
        $tags = @()
    }
    $tags = @($tags | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.Substring(1) } -Descending)
    foreach ($tag in ($tags | Select-Object -Skip $KeepReleases)) {
        & gh release delete $tag --repo $Repo --yes --cleanup-tag
        if ($LASTEXITCODE -ne 0) { Write-Warning "Could not delete release $tag." } else { Write-Host "  pruned release $tag" }
    }
    $ErrorActionPreference = 'Stop'
}

# catalog.json "host" entry (written as text so the rest of the file keeps its formatting)
function ConvertTo-JsonString([string]$value) {
    $sb = New-Object Text.StringBuilder
    [void]$sb.Append('"')
    foreach ($c in $value.ToCharArray()) {
        switch ($c) {
            '"' { [void]$sb.Append('\"') }
            '\' { [void]$sb.Append('\\') }
            "`n" { [void]$sb.Append('\n') }
            "`r" { [void]$sb.Append('\r') }
            "`t" { [void]$sb.Append('\t') }
            default { if ([int]$c -lt 0x20) { [void]$sb.AppendFormat('\u{0:x4}', [int]$c) } else { [void]$sb.Append($c) } }
        }
    }
    [void]$sb.Append('"')
    $sb.ToString()
}
$nl = if ($catalogText.Contains("`r`n")) { "`r`n" } else { "`n" }
$lines = New-Object Collections.Generic.List[string]
$lines.Add('  "host": {')
$lines.Add('    "version": ' + (ConvertTo-JsonString $Version) + ',')
$lines.Add('    "installer": ' + (ConvertTo-JsonString $InstallerUrl) + ',')
$lines.Add('    "sha256": ' + (ConvertTo-JsonString $sha256) + ',')
$lines.Add('    "size": ' + $size + ',')
$lines.Add('    "releaseNotes": [')
for ($i = 0; $i -lt $releaseNotes.Count; $i++) {
    $n = $releaseNotes[$i]
    $lines.Add('      {')
    $lines.Add('        "version": ' + (ConvertTo-JsonString $n.version) + ',')
    if ($n.date) { $lines.Add('        "date": ' + (ConvertTo-JsonString $n.date) + ',') }
    $lines.Add('        "notes": [')
    $items = @($n.notes)
    for ($j = 0; $j -lt $items.Count; $j++) {
        $lines.Add('          ' + (ConvertTo-JsonString $items[$j]) + $(if ($j -lt $items.Count - 1) { ',' } else { '' }))
    }
    $lines.Add('        ]')
    $lines.Add('      }' + $(if ($i -lt $releaseNotes.Count - 1) { ',' } else { '' }))
}
$lines.Add('    ]')
$lines.Add('  },')
$block = ($lines -join $nl) + $nl

$existingHost = [regex]::Match($catalogText, '(?ms)^  "host": \{.*?^  \},?\r?\n')
if ($existingHost.Success) {
    $updated = $catalogText.Substring(0, $existingHost.Index) + $block + $catalogText.Substring($existingHost.Index + $existingHost.Length)
} else {
    # New entry right after "schema".
    $schema = [regex]::Match($catalogText, '(?m)^  "schema": \d+,\r?\n')
    if (-not $schema.Success) { throw "Could not find `"schema`" in $Catalog to place the host entry after." }
    $at = $schema.Index + $schema.Length
    $updated = $catalogText.Substring(0, $at) + $block + $catalogText.Substring($at)
}
$parsed = $updated | ConvertFrom-Json
if ($parsed.host.version -ne $Version -or $parsed.host.sha256 -ne $sha256) { throw 'catalog.json did not round-trip; nothing written.' }
[IO.File]::WriteAllText($Catalog, $updated, (New-Object Text.UTF8Encoding $false))

Write-Host "Host $Version -> $Catalog" -ForegroundColor Green
Write-Host "  installer $InstallerUrl"
Write-Host "  sha256    $sha256 ($size bytes)"
if (-not $SkipRelease) { Write-Host 'Next: commit and push catalog.json so running apps find the update.' -ForegroundColor Yellow }
