#requires -Version 5.1
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$rendererRoot = Join-Path $projectRoot '.tools\icon-renderer'
$packageFile = Join-Path $rendererRoot 'node_modules\@resvg\resvg-js\package.json'
$rendererVersion = '2.6.2'
if (!(Test-Path -LiteralPath $packageFile) -or ((Get-Content -LiteralPath $packageFile -Raw | ConvertFrom-Json).version -ne $rendererVersion)) {
    New-Item -ItemType Directory -Path $rendererRoot -Force | Out-Null
    & npm.cmd install --prefix $rendererRoot --no-audit --no-fund --save-exact "@resvg/resvg-js@$rendererVersion"
    if ($LASTEXITCODE -ne 0) { throw 'Icon renderer installation failed.' }
}
& node (Join-Path $PSScriptRoot 'render-icon.cjs') $projectRoot $rendererRoot
if ($LASTEXITCODE -ne 0) { throw 'Icon rendering failed.' }
