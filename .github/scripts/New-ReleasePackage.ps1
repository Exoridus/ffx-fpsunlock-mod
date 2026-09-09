<#
.SYNOPSIS
    Packages the staged mod payload as <ModId>-mod-<Tag>.zip and writes a .sha256 next to it.

.DESCRIPTION
    StageDir is what build.ps1 -OutDir produces: mods/<ModId>/ holding the dll, manifest,
    deps.json and lang/. The archive keeps the mods/ prefix, so extracting it into the game's
    fahrenheit/ directory puts the mod exactly where Fahrenheit's loader looks for it.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Tag,

    [Parameter(Mandatory = $true)]
    [string] $StageDir,

    [Parameter(Mandatory = $true)]
    [string] $OutDir,

    [string] $ModId = 'fhfpsunlock'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path $StageDir)) {
    throw "Stage directory not found: $StageDir. Run build.ps1 -OutDir first."
}
$stageRoot = (Resolve-Path $StageDir).Path
$modDir = Join-Path $stageRoot "mods/$ModId"

foreach ($required in @("$ModId.dll", "$ModId.manifest.json")) {
    if (-not (Test-Path (Join-Path $modDir $required))) {
        throw "Missing $required in $modDir. Run build.ps1 -OutDir first."
    }
}

# The whole stage directory becomes the archive, so anything beside mods/<ModId>/ would ship.
$unexpected = @(Get-ChildItem $stageRoot | Where-Object Name -ne 'mods') +
              @(Get-ChildItem (Join-Path $stageRoot 'mods') | Where-Object Name -ne $ModId)
if ($unexpected.Count -gt 0) {
    throw "Stage directory contains more than mods/$ModId/: $($unexpected.FullName -join ', ')"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$outRoot = (Resolve-Path $OutDir).Path
$zip = Join-Path $outRoot "$ModId-mod-$Tag.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

# ZipFile rather than Compress-Archive: it writes forward-slash entry names and
# no top-level folder, matching the layout the other Fahrenheit mod releases use.
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stageRoot, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entries = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith('/') } | ForEach-Object FullName)
} finally {
    $archive.Dispose()
}
$prefix = "mods/$ModId/"
$misplaced = @($entries | Where-Object { -not $_.StartsWith($prefix) })
if ($misplaced.Count -gt 0) {
    throw "Archive entries outside ${prefix}: $($misplaced -join ', ')"
}

# sha256sum -c format: hex, two spaces, file name, LF. sha256sum rejects CRLF lines.
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToUpperInvariant()
[System.IO.File]::WriteAllText("$zip.sha256", "$hash  $([System.IO.Path]::GetFileName($zip))`n")

Write-Host "Packaged $zip ($([math]::Round((Get-Item $zip).Length / 1KB, 1)) KB):"
$entries | ForEach-Object { Write-Host "  $_" }
Write-Host "Checksum: $zip.sha256"
