param(
 [Parameter(Mandatory = $true)][string]$RuntimeRoot,
 [Parameter(Mandatory = $true)][string]$PackageDirectory,
 [string]$GameDirectory,
 [string]$MapName = 'sp_boomtown_start',
 [switch]$Graphics
)
$ErrorActionPreference = 'Stop'
$RuntimeRoot = (Resolve-Path -LiteralPath $RuntimeRoot).Path
$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$sourceBuild = Join-Path $RuntimeRoot '..\engine\Tools\SboxBuild\SboxBuild.csproj'
if (!(Test-Path -LiteralPath $sourceBuild) -and !(Test-Path -LiteralPath (Join-Path $RuntimeRoot '.titanfall2-build-runtime'))) {
 throw 'Use a dedicated official source build or a marked test runtime; do not run fixtures in a Steam installation.'
}
$running = @(Get-Process sbox,sbox-dev -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($RuntimeRoot + '\', [StringComparison]::OrdinalIgnoreCase) })
if ($running.Count -gt 0) { throw 'This runtime has a running editor/game. Choose a separate validation runtime.' }
if ($GameDirectory -and !$Graphics) { throw 'Real-map render verification requires -Graphics.' }
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$output = Join-Path $repositoryRoot ('artifacts\native-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$assemblyPath = Join-Path $output 'package.local.titanfall2.dll'
Copy-Item -LiteralPath (Join-Path $PackageDirectory 'package.local.titanfall2.dll') -Destination $assemblyPath
if (Test-Path -LiteralPath (Join-Path $PackageDirectory 'Assets')) { Copy-Item -LiteralPath (Join-Path $PackageDirectory 'Assets') -Destination (Join-Path $output 'Assets') -Recurse }
$previousEngine = $env:FACEPUNCH_ENGINE
$previousGraphics = $env:SBOX_TEST_GRAPHICS
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
try {
 $env:TEMP = Join-Path $output 'temp'
 $env:TMP = $env:TEMP
 New-Item -ItemType Directory -Path $env:TEMP | Out-Null
 & dotnet build (Join-Path $PSScriptRoot 'RuntimeValidation.csproj') -c Release "-p:SboxEngineRoot=$RuntimeRoot" "-p:Titanfall2AssemblyPath=$assemblyPath" -o $output
 if ($LASTEXITCODE -ne 0) { throw 'Native validation fixture failed to build.' }
 $report = Join-Path $output 'runtime-results.json'
 $arguments = @($report)
 if ($GameDirectory) { $arguments += @((Resolve-Path -LiteralPath $GameDirectory).Path, $MapName) }
 $env:FACEPUNCH_ENGINE = $RuntimeRoot
 $env:SBOX_TEST_GRAPHICS = if ($Graphics) { '1' } else { '0' }
 Push-Location -LiteralPath $RuntimeRoot
 try { & (Join-Path $output 'Sandbox.Test.Integration.exe') @arguments }
 finally { Pop-Location }
 if (!(Test-Path -LiteralPath $report) -or !(Get-Content -LiteralPath $report -Raw | ConvertFrom-Json).success) { throw "Native validation failed. See $report" }
 Write-Host "Native verification report: $report"
}
finally {
 $env:FACEPUNCH_ENGINE = $previousEngine
 $env:SBOX_TEST_GRAPHICS = $previousGraphics
 $env:TEMP = $previousTemp
 $env:TMP = $previousTmp
}
