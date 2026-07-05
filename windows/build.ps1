# Agent Island for Windows — release build.
# Produces a self-contained single-file exe plus a distributable zip under dist\.
param([string]$Runtime = "win-x64", [string]$Version = "")

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$csproj = "src\AgentIsland\AgentIsland.csproj"
$version = $Version
if (-not $version) {
    $version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version
    if (-not $version) { $version = "0.0.0" }
}
$version = "$version".Trim()

$publishDir = "dist\publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

& dotnet publish $csproj `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir `
    -p:Version=$version
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$zip = "dist\AgentIsland-$version-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$publishDir\AgentIsland.exe" -DestinationPath $zip

Write-Output "built  $publishDir\AgentIsland.exe"
Write-Output "packed $zip"
