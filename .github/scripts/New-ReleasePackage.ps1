<#
.SYNOPSIS
    Packages the staged mod payload as <ModId>-mod-<Tag>.zip and, given a Fahrenheit deploy
    tree, the ready-to-play fahrenheit-full-<Tag>.zip. Writes a .sha256 next to each.

.DESCRIPTION
    StageDir is what build.ps1 -OutDir produces: mods/<ModId>/ holding the dll, manifest,
    deps.json and lang/. The mod archive keeps the mods/ prefix, so extracting it into the
    game's fahrenheit/ directory puts the mod exactly where Fahrenheit's loader looks for it.

    FahrenheitDeployDir is the artifacts/deploy/rel tree tools/build-fahrenheit.ps1 leaves in
    the pinned checkout. The full archive extracts into the game directory itself: fahrenheit/
    with the host's bin/, the mod, a loadorder naming only this mod, the launcher scripts from
    packaging/, and a README. Debug symbols and import libraries are left out.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Tag,

    [Parameter(Mandatory = $true)]
    [string] $StageDir,

    [Parameter(Mandatory = $true)]
    [string] $OutDir,

    [string] $FahrenheitDeployDir,

    [string] $ModId = 'fhfpsunlock'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

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

function Write-Archive([string] $SourceDir, [string] $Zip, [string[]] $AllowedPrefixes) {
    if (Test-Path $Zip) { Remove-Item $Zip -Force }

    # ZipFile rather than Compress-Archive: it writes forward-slash entry names and
    # no top-level folder, matching the layout the other Fahrenheit mod releases use.
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $SourceDir, $Zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $archive = [System.IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        $entries = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith('/') } | ForEach-Object FullName)
    } finally {
        $archive.Dispose()
    }
    $misplaced = @($entries | Where-Object { $e = $_; -not ($AllowedPrefixes | Where-Object { $e.StartsWith($_) }) })
    if ($misplaced.Count -gt 0) {
        throw "Archive entries outside $($AllowedPrefixes -join ', '): $($misplaced -join ', ')"
    }

    # sha256sum -c format: hex, two spaces, file name, LF. sha256sum rejects CRLF lines.
    $hash = (Get-FileHash $Zip -Algorithm SHA256).Hash.ToUpperInvariant()
    [System.IO.File]::WriteAllText("$Zip.sha256", "$hash  $([System.IO.Path]::GetFileName($Zip))`n")

    Write-Host "Packaged $Zip ($([math]::Round((Get-Item $Zip).Length / 1KB, 1)) KB):"
    $entries | ForEach-Object { Write-Host "  $_" }
    Write-Host "Checksum: $Zip.sha256"
    return $entries
}

$modZip = Join-Path $outRoot "$ModId-mod-$Tag.zip"
Write-Archive $stageRoot $modZip @("mods/$ModId/") | Out-Null

if (-not $FahrenheitDeployDir) {
    return
}

if (-not (Test-Path $FahrenheitDeployDir)) {
    throw "Fahrenheit deploy directory not found: $FahrenheitDeployDir. Run tools/build-fahrenheit.ps1 first."
}
$deployBin = Join-Path (Resolve-Path $FahrenheitDeployDir).Path 'bin'
foreach ($required in @('fhstage0.exe', 'fhstage1.dll', 'fh.dll', 'fhr.dll')) {
    if (-not (Test-Path (Join-Path $deployBin $required))) {
        throw "Missing $required in $deployBin. Run tools/build-fahrenheit.ps1 first."
    }
}

$fullStage = Join-Path $outRoot 'stage-full'
if (Test-Path $fullStage) { Remove-Item $fullStage -Recurse -Force }
$fullRoot = Join-Path $fullStage 'fahrenheit'
New-Item -ItemType Directory -Force -Path (Join-Path $fullRoot 'bin') | Out-Null

# The host, without its debug symbols and the import library the stage1 link leaves behind.
Get-ChildItem $deployBin -Recurse -File |
    Where-Object { $_.Extension -notin @('.pdb', '.lib', '.exp') } |
    ForEach-Object {
        $relative = $_.FullName.Substring($deployBin.Length).TrimStart('\', '/')
        $target = Join-Path (Join-Path $fullRoot 'bin') $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item $_.FullName $target
    }

Copy-Item (Join-Path $stageRoot 'mods') $fullRoot -Recurse
# One line, LF, no trailing newline needed: Fahrenheit reads the file line by line.
[System.IO.File]::WriteAllText((Join-Path $fullRoot 'mods/loadorder'), "$ModId`n")

Copy-Item (Join-Path $repoRoot 'packaging/start-fahrenheit.cmd') $fullRoot
Copy-Item (Join-Path $repoRoot 'packaging/start-ffx-with-mods.cmd') $fullStage
Copy-Item (Join-Path $repoRoot 'packaging/README.txt') $fullStage

$fullZip = Join-Path $outRoot "fahrenheit-full-$Tag.zip"
$entries = Write-Archive $fullStage $fullZip @('fahrenheit/', 'start-ffx-with-mods.cmd', 'README.txt')
foreach ($required in @("fahrenheit/mods/$ModId/$ModId.dll", 'fahrenheit/mods/loadorder', 'fahrenheit/bin/fhstage0.exe', 'fahrenheit/start-fahrenheit.cmd')) {
    if ($entries -notcontains $required) {
        throw "Full archive is missing $required"
    }
}
Remove-Item $fullStage -Recurse -Force
