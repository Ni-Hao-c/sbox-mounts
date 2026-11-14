param(
 [string]$EngineSource,
 [string]$EngineRoot,
 [string]$OutputDirectory,
 [switch]$Test,
 [switch]$CompileShaders
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..'))
if (!$EngineSource) { $EngineSource = $env:SBOX_ENGINE_SOURCE }
if (!$EngineRoot) { $EngineRoot = $env:FACEPUNCH_ENGINE }
if (!$EngineSource -and $EngineRoot) { $EngineSource = Join-Path $EngineRoot '..' }
if (!$EngineSource) { throw 'Pass -EngineSource <official sbox-public checkout> or set SBOX_ENGINE_SOURCE. The checkout must support SboxBuild build-mount.' }
$EngineSource = (Resolve-Path -LiteralPath $EngineSource).Path
$EngineRoot = Join-Path $EngineSource 'game'
$buildProject = Join-Path $EngineSource 'engine\Tools\SboxBuild\SboxBuild.csproj'
if (!(Test-Path -LiteralPath $buildProject)) { throw "SboxBuild not found: $buildProject" }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repositoryRoot 'artifacts\titanfall2-official' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ($OutputDirectory.Equals($EngineRoot, [StringComparison]::OrdinalIgnoreCase) -or $OutputDirectory.StartsWith($EngineRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
 throw 'Use an output directory outside game/. Install the completed mount separately.'
}
# SboxBuild deletes/recreates only its per-mount output. Never allow project/source folders.
$packageDirectory = Join-Path $OutputDirectory 'Titanfall2'
foreach ($sourceRoot in @($projectRoot, $EngineSource)) {
 if ($sourceRoot.Equals($packageDirectory, [StringComparison]::OrdinalIgnoreCase) -or $sourceRoot.StartsWith($packageDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) {
  throw "Output would overwrite source: $packageDirectory"
 }
}
if ($packageDirectory.StartsWith($projectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Build outside the mount source folder.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
try {
 $env:TEMP = Join-Path $OutputDirectory '.build-temp'
 $env:TMP = $env:TEMP
 New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
if ($CompileShaders) { Write-Host 'The official build-mount pipeline always compiles shaders and content.' }
if ($Test) {
 & dotnet test (Join-Path $projectRoot 'Tests\Titanfall2.Tests.csproj') -c Release "-p:SboxEngineRoot=$EngineRoot" --logger 'trx;LogFileName=migration.trx'
 if ($LASTEXITCODE -ne 0) { throw 'Titanfall 2 regression tests failed.' }
}
Push-Location -LiteralPath $EngineSource
try {
 $builder = Join-Path $EngineSource 'engine\Tools\SboxBuild\bin\net10.0\sboxbuild.exe'
 $arguments = @('build-mount', '--project', (Join-Path $projectRoot '.sbproj'), '--out', $OutputDirectory)
 if (Test-Path -LiteralPath $builder) { & $builder @arguments }
 else { & dotnet run --project $buildProject -- @arguments }
 if ($LASTEXITCODE -ne 0) { throw 'Official sandboxed mount build failed.' }
}
finally { Pop-Location }
$assembly = Join-Path $packageDirectory 'package.local.titanfall2.dll'
if (!(Test-Path -LiteralPath $assembly)) { throw "Official package assembly missing: $assembly" }
Write-Host "Official Titanfall 2 mount: $packageDirectory"
Get-FileHash -LiteralPath $assembly -Algorithm SHA256

}
finally { $env:TEMP = $previousTemp; $env:TMP = $previousTmp }
