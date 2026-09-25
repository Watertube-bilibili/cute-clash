#requires -Version 5.1
param([switch]$RunTests, [switch]$Package)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Install .NET Framework 4.8 before building.' }
$yaml = Join-Path $projectRoot 'dependencies\YamlDotNet\YamlDotNet.dll'
if (!(Test-Path -LiteralPath $yaml)) { throw 'Run scripts\fetch-dependencies.ps1 first.' }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$references = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll',"/r:$yaml")
$icon = Join-Path $projectRoot 'assets\cute-clash.ico'
if (!(Test-Path -LiteralPath $icon)) { throw 'Missing assets\cute-clash.ico. Rebuild the icon with scripts\render-icon.cjs if needed.' }
$branding = @("/win32icon:$icon", "/resource:$icon,CuteClash.AppIcon")
$distRoot = Join-Path $projectRoot 'dist'
foreach ($arch in @('x64','x86')) {
    $output = Join-Path $distRoot "cute-clash-$arch"
    New-Item -ItemType Directory -Force -Path (Join-Path $output 'core') | Out-Null
    & $compiler /nologo /target:winexe /optimize+ /utf8output /codepage:65001 /langversion:5 "/platform:$arch" "/win32manifest:$projectRoot\src\app.manifest" "/out:$output\cute-clash.exe" @branding @references @sources
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $arch" }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\App.config') -Destination (Join-Path $output 'cute-clash.exe.config') -Force
    Copy-Item -LiteralPath $yaml -Destination $output -Force
    Copy-Item -Path (Join-Path $projectRoot "dependencies\mihomo\$arch\*") -Destination (Join-Path $output 'core') -Force
    foreach ($folder in @('licenses','examples','docs','assets')) {
        if (Test-Path -LiteralPath (Join-Path $projectRoot $folder)) { Copy-Item -LiteralPath (Join-Path $projectRoot $folder) -Destination $output -Recurse -Force }
    }
    foreach ($file in @('README.md','README.en.md','CHANGELOG.md','LICENSE','THIRD-PARTY-NOTICES.md','dependencies.lock.json')) {
        if (Test-Path -LiteralPath (Join-Path $projectRoot $file)) { Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $output -Force }
    }
    Write-Output "Built $output\cute-clash.exe"
}
if ($RunTests) {
    $tests = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests') -Filter '*.cs' | ForEach-Object { $_.FullName })
    foreach ($arch in @('x64','x86')) {
        $testOutput = Join-Path $projectRoot "artifacts\tests\$arch"
        New-Item -ItemType Directory -Force -Path $testOutput | Out-Null
        & $compiler /nologo /target:exe /optimize+ /utf8output /codepage:65001 /langversion:5 "/platform:$arch" "/win32manifest:$projectRoot\src\app.manifest" /main:CuteClash.Tests.TestRunner "/out:$testOutput\tests.exe" @branding @references @sources @tests
        if ($LASTEXITCODE -ne 0) { throw "Test compilation failed: $arch" }
        Copy-Item -LiteralPath $yaml -Destination $testOutput -Force
        & (Join-Path $testOutput 'tests.exe') $projectRoot
        if ($LASTEXITCODE -ne 0) { throw "Tests failed: $arch" }
    }
}
if ($Package) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $sourceZip = Join-Path $distRoot 'cute-clash-0.3.1-framework-source.zip'
    if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip }
    $archive = [IO.Compression.ZipFile]::Open($sourceZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $sourceFiles = @()
        foreach ($folder in @('src','scripts','tests','examples','docs','licenses','assets','installer','.github')) {
            $sourceFiles += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -File -Recurse)
        }
        foreach ($name in @('README.md','README.en.md','CHANGELOG.md','LICENSE','PRODUCT.md','DESIGN.md','dependencies.lock.json','runtime.lock.json','cute-clash.csproj','cute-clash.selfcontained.csproj','.gitignore','.gitattributes')) {
            if (Test-Path -LiteralPath (Join-Path $projectRoot $name)) { $sourceFiles += Get-Item -LiteralPath (Join-Path $projectRoot $name) }
        }
        $sourceFiles += Get-Item -LiteralPath (Join-Path $projectRoot 'dependencies\sources\mihomo-v1.19.31-source.zip')
        foreach ($file in $sourceFiles) {
            $entry = $file.FullName.Substring($projectRoot.Length + 1).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
    foreach ($arch in @('x64','x86')) {
        $bundledSources = Join-Path (Join-Path $distRoot "cute-clash-$arch") 'sources'
        New-Item -ItemType Directory -Path $bundledSources -Force | Out-Null
        Copy-Item -LiteralPath $sourceZip -Destination $bundledSources -Force
        $zip = Join-Path $distRoot "cute-clash-0.3.1-framework-$arch.zip"
        if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
        [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $distRoot "cute-clash-$arch"), $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
        Write-Output "Packaged $zip"
    }
    Get-ChildItem -LiteralPath $distRoot -Filter 'cute-clash-0.3.1-framework-*.zip' | ForEach-Object {
        $digest = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
        '{0}  {1}' -f $digest.Hash.ToLowerInvariant(), $_.Name
    } | Set-Content -LiteralPath (Join-Path $distRoot 'SHA256SUMS-framework.txt') -Encoding ASCII
}
