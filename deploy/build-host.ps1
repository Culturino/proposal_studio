# Publishes the API with the React app baked into wwwroot for the Linux host.
$ErrorActionPreference = "Stop"
$ApiRoot = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent $ApiRoot
$Frontend = Join-Path $RepoRoot "proposal-frontend"
$Out = Join-Path $PSScriptRoot "publish"

Write-Host "Building frontend..."
Push-Location $Frontend
try {
    if (-not (Test-Path "node_modules")) {
        npm install
        if ($LASTEXITCODE -ne 0) { throw "npm install failed" }
    }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed" }
}
finally {
    Pop-Location
}

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
Write-Host "Publishing API for linux-x64..."
dotnet publish (Join-Path $ApiRoot "ProposalStudio.csproj") -c Release -r linux-x64 --self-contained true -o $Out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$www = Join-Path $Out "wwwroot"
New-Item -ItemType Directory -Path $www -Force | Out-Null
$srcWww = Join-Path $ApiRoot "wwwroot"
if (Test-Path $srcWww) {
    Copy-Item (Join-Path $srcWww "*") $www -Recurse -Force
}
$dist = Join-Path $Frontend "dist"
Copy-Item (Join-Path $dist "index.html") (Join-Path $www "index.html") -Force
if (Test-Path (Join-Path $dist "assets")) {
    $assetsDest = Join-Path $www "assets"
    if (Test-Path $assetsDest) { Remove-Item $assetsDest -Recurse -Force }
    Copy-Item (Join-Path $dist "assets") $assetsDest -Recurse -Force
}
if (Test-Path (Join-Path $dist "fonts")) {
    $fontsDest = Join-Path $www "fonts"
    if (Test-Path $fontsDest) { Remove-Item $fontsDest -Recurse -Force }
    Copy-Item (Join-Path $dist "fonts") $fontsDest -Recurse -Force
}
Get-ChildItem $dist -File | Where-Object { $_.Name -ne "index.html" } | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $www $_.Name) -Force
}

Write-Host "Publish ready at $Out"
