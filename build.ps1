param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Deploy,
    # Stages the mod into <OutDir>/mods/<mod id>/ with the exact layout -Deploy writes into the
    # game's fahrenheit/ directory, so CI can package a release without a game install.
    [string]$OutDir
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

function Copy-ModPayload([string]$Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item $Dll $Destination -Force
    Copy-Item $ManifestFile.FullName $Destination -Force
    $DepsJson = Join-Path $BuildOutput "$ModId.deps.json"
    if (Test-Path $DepsJson) { Copy-Item $DepsJson $Destination -Force }

    # Localization: Fahrenheit expects mods/<id>/lang/<module type name>/<locale>.json, and the
    # settings panel falls back to the raw setting id when a key is missing - so a deploy without
    # this looks like the labels were never written.
    $LangDir = Join-Path $ProjectRoot 'lang'
    if (Test-Path $LangDir) {
        # Copy the contents, not the directory: Copy-Item nests a source directory inside an
        # existing destination directory, which turned a second deploy into lang/lang/.
        $TargetLang = Join-Path $Destination 'lang'
        New-Item -ItemType Directory -Force -Path $TargetLang | Out-Null
        Copy-Item (Join-Path $LangDir '*') $TargetLang -Recurse -Force
    }
}

if ($Deploy) {
    $DeployDir = Join-Path 'C:\Games\Final Fantasy X-X2 - HD Remaster\fahrenheit\mods' $ModId
    Copy-ModPayload $DeployDir
    Write-Host "Deployed: $DeployDir" -ForegroundColor Green
}

if ($OutDir) {
    $StageDir = Join-Path (Join-Path $OutDir 'mods') $ModId
    # A release zip is built from this directory, so nothing from an earlier stage may survive.
    $StageDir = [System.IO.Path]::GetFullPath($StageDir)
    $StageRoot = [System.IO.Path]::GetFullPath($OutDir).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    if (-not $StageDir.StartsWith($StageRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Staging destination must stay inside OutDir.'
    }
    if (Test-Path -LiteralPath $StageDir) { Remove-Item -LiteralPath $StageDir -Recurse -Force }
    Copy-ModPayload $StageDir
    Write-Host "Staged: $StageDir" -ForegroundColor Green
}
