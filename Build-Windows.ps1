$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'CleanSweep.csproj'
$publishDirectory = Join-Path $PSScriptRoot 'publish\win-x64'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 10 SDK not found. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and reopen PowerShell.'
}

$sdkList = dotnet --list-sdks
if ($LASTEXITCODE -ne 0 -or -not ($sdkList -match '10\.')) {
    throw '.NET 10 SDK is required. Check installed SDKs with: dotnet --list-sdks'
}

Write-Host 'Publishing CleanSweep for Windows x64...' -ForegroundColor Cyan

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --property:PublishSingleFile=true `
    --property:IncludeNativeLibrariesForSelfExtract=true `
    --property:EnableCompressionInSingleFile=true `
    --output $publishDirectory

if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed. Review build errors above.'
}

$exe = Join-Path $publishDirectory 'CleanSweep.exe'
$settings = Join-Path $publishDirectory 'localsetting.json'

if (-not (Test-Path $exe)) {
    throw "Publish finished without expected EXE: $exe"
}

if (-not (Test-Path $settings)) {
    throw "Publish finished without localsetting.json beside the EXE: $settings"
}

Write-Host "Publish successful: $exe" -ForegroundColor Green
Write-Host "Configuration file: $settings" -ForegroundColor Green
