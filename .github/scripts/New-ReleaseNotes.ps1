<#
.SYNOPSIS
    Writes Markdown release notes for Tag from the commits since the previous tag.

.DESCRIPTION
    The range ends at HEAD rather than at Tag, because the tag is created only after this
    script ran (see release.yml) and never exists for the CI preflight tag. Without a previous
    tag the notes list the full history.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Tag,

    # owner/name slug, as GitHub exposes it in GITHUB_REPOSITORY.
    [Parameter(Mandatory = $true)]
    [string] $Repository,

    [Parameter(Mandatory = $true)]
    [string] $OutFile,

    [string] $ModId = 'fhfpsunlock'
)

$ErrorActionPreference = 'Stop'

$repoUrl = "https://github.com/$Repository"
$project = $Repository.Split('/')[-1]
$asset = "$ModId-mod-$Tag.zip"
$assetUrl = "$repoUrl/releases/download/$Tag/$asset"

# --exclude keeps a pre-existing Tag (dry runs against a released commit) from
# being reported as its own predecessor.
$previousTag = git describe --tags --abbrev=0 --match 'v[0-9]*' --exclude $Tag HEAD 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($previousTag)) {
    $previousTag = ''
}
$previousTag = $previousTag.Trim()

$range = if ($previousTag) { "$previousTag..HEAD" } else { 'HEAD' }
$commits = @(git log --no-merges --pretty=format:"- %s ([%h]($repoUrl/commit/%H))" $range)
if ($LASTEXITCODE -ne 0) {
    throw "git log $range failed."
}
$commits = @($commits | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($commits.Count -eq 0) {
    $commits = @('- Initial release')
}

$releaseDate = git log -1 --date=short --format=%ad HEAD
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($releaseDate)) {
    $releaseDate = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
}

$changelog = if ($previousTag) { "$repoUrl/compare/$previousTag...$Tag" } else { "$repoUrl/commits/$Tag" }

$lines = @(
    "# $project $Tag ($releaseDate)",
    '',
    '**Experimental preview. Build success does not establish in-game compatibility. See the README for known limitations.**',
    '',
    'Pre-built mod package for Final Fantasy X HD Remaster running under Fahrenheit:',
    '',
    "- [$asset]($assetUrl)",
    "  - SHA256: [$asset.sha256]($assetUrl.sha256)",
    '',
    '## Installation',
    '',
    '1. Install Fahrenheit and start the game once, so `fahrenheit/` exists next to `FFX.exe`.',
    "2. Extract ``$asset`` into that ``fahrenheit/`` directory. It contains ``mods/$ModId/``.",
    "3. Add ``$ModId`` on a line of its own to ``fahrenheit/mods/loadorder``.",
    '',
    '## Changes in This Release',
    '',
    "[View this tag]($repoUrl/releases/tag/$Tag) | [All Releases]($repoUrl/releases)",
    ''
) + $commits + @(
    '',
    '---',
    '',
    "Full Changelog: $changelog | [README]($repoUrl/blob/main/README.md)",
    ''
)

$outDir = Split-Path -Parent $OutFile
if ($outDir) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
[System.IO.File]::WriteAllText($OutFile, ($lines -join "`n"))

Write-Host "Release notes written to $OutFile ($($commits.Count) commit line(s), previous tag: $(if ($previousTag) { $previousTag } else { 'none' }))."
