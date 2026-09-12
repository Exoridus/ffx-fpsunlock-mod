# Builds the Fahrenheit host from the pinned checkout in .workspace/fahrenheit into its own
# artifacts/deploy/<rel|dbg> tree: the managed core and runtime through dotnet, and the native
# stage0/stage1 loaders through MSBuild. That tree is what the full release package wraps
# around the mod.
#
# Windows only. The loaders need Visual Studio (or Build Tools) with the C++ workload and a
# vcpkg integrated into MSBuild (`vcpkg integrate install`), because both projects use a vcpkg
# manifest. The managed part needs nothing beyond the .NET SDK the mod itself needs.
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    # Explicit MSBuild.exe; resolved through vswhere when empty.
    [string]$MSBuildExe,
    # Explicit VC++ platform toolset (v143, v145, ...). The loader projects pin the toolset that
    # upstream builds with; a machine that carries a different one needs the override, so the
    # newest installed one is probed when this is empty.
    [string]$PlatformToolset
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$FahrenheitDir = Join-Path $Root '.workspace/fahrenheit'
if (-not (Test-Path (Join-Path $FahrenheitDir 'Fahrenheit.slnx'))) {
    throw "Fahrenheit source not found at $FahrenheitDir. Run tools/bootstrap.ps1 first."
}

$flavor = if ($Configuration -eq 'Debug') { 'dbg' } else { 'rel' }
$DeployDir = Join-Path $FahrenheitDir "artifacts/deploy/$flavor"

function Resolve-VsInstallPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path $vswhere)) { return '' }
    $path = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if ($LASTEXITCODE -ne 0 -or -not $path) { return '' }
    return ($path | Select-Object -First 1).Trim()
}

$vsPath = Resolve-VsInstallPath

if (-not $MSBuildExe) {
    if ($vsPath) {
        $candidate = Join-Path $vsPath 'MSBuild/Current/Bin/MSBuild.exe'
        if (Test-Path $candidate) { $MSBuildExe = $candidate }
    }
    if (-not $MSBuildExe) {
        $onPath = Get-Command msbuild.exe -ErrorAction SilentlyContinue
        if ($onPath) { $MSBuildExe = $onPath.Source }
    }
    if (-not $MSBuildExe) {
        throw 'MSBuild.exe not found. Install Visual Studio Build Tools with the C++ workload, or pass -MSBuildExe.'
    }
}

if (-not $PlatformToolset -and $vsPath) {
    $vcRoot = Join-Path $vsPath 'MSBuild/Microsoft/VC'
    if (Test-Path $vcRoot) {
        $vcVersions = Get-ChildItem $vcRoot -Directory -Filter 'v*' | Sort-Object Name -Descending
        :probe foreach ($vc in $vcVersions) {
            foreach ($candidate in @('v145', 'v144', 'v143', 'v142')) {
                if (Test-Path (Join-Path $vc.FullName "Platforms/Win32/PlatformToolsets/$candidate/Toolset.props")) {
                    $PlatformToolset = $candidate
                    break probe
                }
            }
        }
    }
}

Write-Host "Fahrenheit : $FahrenheitDir ($(git -C $FahrenheitDir rev-parse --short HEAD))"
Write-Host "MSBuild    : $MSBuildExe"
Write-Host "Toolset    : $(if ($PlatformToolset) { $PlatformToolset } else { '(project default)' })"
Write-Host "Deploy     : $DeployDir"

# A tree left by an earlier, wider build (the editor tools, for one) would otherwise ship.
if (Test-Path $DeployDir) { Remove-Item $DeployDir -Recurse -Force }

# Each managed project publishes itself into the deploy tree from its own AfterBuild hook
# (Directory.Build.targets in the Fahrenheit tree), so building is deploying.
foreach ($project in @('src/core/Fahrenheit.csproj', 'src/runtime/Fahrenheit.Runtime.csproj')) {
    $full = Join-Path $FahrenheitDir $project
    & dotnet build $full -c $Configuration --verbosity quiet -p:SolutionDir="$FahrenheitDir/"
    if ($LASTEXITCODE -ne 0) { throw "dotnet build $project failed with exit $LASTEXITCODE" }
}

$toolsetArg = if ($PlatformToolset) { "/p:PlatformToolset=$PlatformToolset" } else { $null }
foreach ($project in @('src/stage0/Fahrenheit.Loader.Stage0.vcxproj', 'src/stage1/Fahrenheit.Loader.Stage1.vcxproj')) {
    $full = Join-Path $FahrenheitDir $project
    & $MSBuildExe $full /nologo /m /v:minimal /p:Configuration=$Configuration /p:Platform=Win32 $toolsetArg
    if ($LASTEXITCODE -ne 0) { throw "msbuild $project failed with exit $LASTEXITCODE" }
}

$bin = Join-Path $DeployDir 'bin'
foreach ($required in @('fhstage0.exe', 'fhstage1.dll', 'fh.dll', 'fhr.dll')) {
    if (-not (Test-Path (Join-Path $bin $required))) {
        throw "Fahrenheit build did not produce $required in $bin"
    }
}
Write-Host "Fahrenheit built: $bin" -ForegroundColor Green
