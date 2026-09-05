param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Deploy
)

$ErrorActionPreference = 'Stop'
$ProjectRoot  = $PSScriptRoot
$ProjectFile  = Get-ChildItem $ProjectRoot -Filter 'Fahrenheit.Mods.*.csproj' | Select-Object -First 1
$ManifestFile = Get-ChildItem $ProjectRoot -Filter '*.manifest.json' | Select-Object -First 1
$ModId = [System.IO.Path]::GetFileNameWithoutExtension($ManifestFile.Name) -replace '\.manifest$', ''

if (-not (Test-Path (Join-Path $ProjectRoot '.workspace/fahrenheit'))) {
    throw "Fahrenheit source not found. Run tools/bootstrap.ps1 first."
}

& dotnet build $ProjectFile.FullName -c $Configuration --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit $LASTEXITCODE" }

$BuildOutput = Join-Path $ProjectRoot "bin/$Configuration/net10.0/win-x86"
$Dll = Join-Path $BuildOutput "$ModId.dll"
if (-not (Test-Path $Dll)) { throw "Build output not found: $Dll" }
Write-Host "Built: $Dll" -ForegroundColor Green

if ($Deploy) {
    $DeployDir = Join-Path 'C:\Games\Final Fantasy X-X2 - HD Remaster\fahrenheit\mods' $ModId
    New-Item -ItemType Directory -Force -Path $DeployDir | Out-Null
    Copy-Item $Dll $DeployDir -Force
    Copy-Item $ManifestFile.FullName $DeployDir -Force
    $DepsJson = Join-Path $BuildOutput "$ModId.deps.json"
    if (Test-Path $DepsJson) { Copy-Item $DepsJson $DeployDir -Force }

    # Localization: Fahrenheit expects mods/<id>/lang/<module type name>/<locale>.json, and the
    # settings panel falls back to the raw setting id when a key is missing - so a deploy without
    # this looks like the labels were never written.
    $LangDir = Join-Path $ProjectRoot 'lang'
    if (Test-Path $LangDir) {
        Copy-Item $LangDir (Join-Path $DeployDir 'lang') -Recurse -Force
    }
    Write-Host "Deployed: $DeployDir" -ForegroundColor Green
}
