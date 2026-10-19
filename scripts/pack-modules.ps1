<#
    Builds the sample modules, packages them and writes the local feed:
      artifacts/feed/catalog.json      catalog/catalog.json with package hashes and sizes filled in
      artifacts/feed/<id>-<ver>.zip    module packages
    Module repositories use the same package layout (see docs/MODULES.md).
#>
[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
$feed = Join-Path $root 'artifacts/feed'
New-Item -ItemType Directory -Force $feed | Out-Null

function New-ModulePackage([string]$projectDir) {
    $manifest = Get-Content (Join-Path $projectDir 'module.json') -Raw | ConvertFrom-Json
    dotnet build $projectDir -c $Configuration -v q -nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $projectDir" }
    $output = Join-Path $projectDir "bin/$Configuration/net48"
    $zipPath = Join-Path $feed "$($manifest.id)-$($manifest.version).zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath }

    $zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
    try {
        Get-ChildItem $output -Recurse -File |
            Where-Object { $_.Name -notlike 'SwissTechKnife.Sdk.*' -and $_.Extension -notin '.pdb', '.xml' } |
            ForEach-Object {
                $relative = $_.FullName.Substring($output.Length).TrimStart('\', '/').Replace('\', '/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $relative, 'Optimal') | Out-Null
            }
    } finally { $zip.Dispose() }

    $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [pscustomobject]@{ Id = $manifest.id; File = Split-Path $zipPath -Leaf; Sha256 = $hash; Size = (Get-Item $zipPath).Length }
}

$packages = @{}
foreach ($project in Get-ChildItem (Join-Path $root 'samples') -Directory) {
    if (Test-Path (Join-Path $project.FullName 'module.json')) {
        $package = New-ModulePackage $project.FullName
        $packages[$package.Id] = $package
        Write-Host "Packed $($package.File) ($($package.Size) bytes)"
    }
}

$catalog = Get-Content (Join-Path $root 'catalog/catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($entry in $catalog.modules) {
    if ($packages.ContainsKey($entry.id)) {
        $p = $packages[$entry.id]
        $entry | Add-Member -Force NoteProperty package $p.File
        $entry | Add-Member -Force NoteProperty sha256 $p.Sha256
        $entry | Add-Member -Force NoteProperty size $p.Size
    }
}
$json = $catalog | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText((Join-Path $feed 'catalog.json'), $json, (New-Object Text.UTF8Encoding $false))
Write-Host "Feed written to $feed" -ForegroundColor Green
