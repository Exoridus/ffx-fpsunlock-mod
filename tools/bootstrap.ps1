# Clones Fahrenheit at the SHA pinned in fahrenheit.release.ref into .workspace/fahrenheit,
# which is where the csproj probes for the core project reference.
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Sha  = (Get-Content -Raw (Join-Path $Root 'fahrenheit.release.ref')).Trim()
$Dest = Join-Path $Root '.workspace/fahrenheit'

if ((Test-Path $Dest) -and -not $Force) {
    Write-Host "Already present: $Dest (use -Force to re-clone)" -ForegroundColor Yellow
} else {
    if (Test-Path $Dest) { Remove-Item -Recurse -Force $Dest }
    New-Item -ItemType Directory -Force -Path (Split-Path $Dest) | Out-Null
    & git clone https://github.com/fahrenheit-crew/fahrenheit.git $Dest
    if ($LASTEXITCODE -ne 0) { throw "git clone failed with exit $LASTEXITCODE" }
}

& git -C $Dest checkout --detach $Sha
if ($LASTEXITCODE -ne 0) { throw "checkout of $Sha failed with exit $LASTEXITCODE" }
Write-Host "Fahrenheit pinned at $Sha" -ForegroundColor Green
