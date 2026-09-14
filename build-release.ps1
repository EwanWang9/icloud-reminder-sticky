param(
    [Parameter(Mandatory = $false)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectRoot 'iCloudReminderSticky.csproj'
$releaseRoot = Join-Path $projectRoot '.release'
$portableSdk = Join-Path (Split-Path -Parent $projectRoot) '.dotnet-sdk\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $portableSdk) { $portableSdk } else { 'dotnet' }
$resolvedProjectRoot = [System.IO.Path]::GetFullPath($projectRoot)
$resolvedReleaseRoot = [System.IO.Path]::GetFullPath($releaseRoot)

if (-not $resolvedReleaseRoot.StartsWith($resolvedProjectRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output must stay inside the project directory.'
}

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseRoot | Out-Null

foreach ($runtime in @('win-x64', 'win-arm64')) {
    $publishDirectory = Join-Path $releaseRoot $runtime
    & $dotnetCommand publish $projectFile `
        -c Release `
        -r $runtime `
        --self-contained true `
        -p:Version=$Version `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -o $publishDirectory

    Get-ChildItem -LiteralPath $publishDirectory -Filter '*.xml' -File | Remove-Item -Force
    $archive = Join-Path $releaseRoot "iCloudReminderSticky-v$Version-$runtime.zip"
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archive -CompressionLevel Optimal
}

$checksumLines = Get-ChildItem -LiteralPath $releaseRoot -Filter '*.zip' | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
}
$checksumLines | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding utf8

Get-ChildItem -LiteralPath $releaseRoot -Filter '*.zip' | Select-Object Name, Length, LastWriteTime
