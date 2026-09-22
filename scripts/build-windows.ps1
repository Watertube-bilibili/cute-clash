#requires -Version 5.1
param([switch]$RunTests, [switch]$Package, [switch]$Installer)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = '0.3.0'
$runtimeLock = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'runtime.lock.json') | ConvertFrom-Json
$sdkDirectory = Join-Path $projectRoot '.tools\dotnet6'
$sdkArchive = Join-Path $projectRoot ('.tools\dotnet-sdk-' + $runtimeLock.sdk.version + '-win-x64.zip')
New-Item -ItemType Directory -Path (Split-Path -Parent $sdkArchive) -Force | Out-Null
if (!(Test-Path -LiteralPath $sdkArchive)) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $pending = $sdkArchive + '.download'
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $runtimeLock.sdk.url -OutFile $pending
        if ((Get-FileHash -LiteralPath $pending -Algorithm SHA512).Hash -ne $runtimeLock.sdk.sha512) { throw 'Microsoft SDK archive SHA512 mismatch.' }
        Move-Item -LiteralPath $pending -Destination $sdkArchive
    } finally { if (Test-Path -LiteralPath $pending) { Remove-Item -LiteralPath $pending } }
}
if ((Get-FileHash -LiteralPath $sdkArchive -Algorithm SHA512).Hash -ne $runtimeLock.sdk.sha512) { throw 'Microsoft SDK archive SHA512 mismatch.' }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (!(Test-Path -LiteralPath (Join-Path $sdkDirectory 'dotnet.exe'))) {
    [IO.Compression.ZipFile]::ExtractToDirectory($sdkArchive, $sdkDirectory)
}
$dotnet = Join-Path $sdkDirectory 'dotnet.exe'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
if ((& $dotnet --version) -ne $runtimeLock.sdk.version) { throw 'Unexpected SDK version.' }
& (Join-Path $PSScriptRoot 'fetch-dependencies.ps1') -VerifyOnly
if ($LASTEXITCODE -ne 0) { throw 'Dependency verification failed.' }
$project = Join-Path $projectRoot 'cute-clash.selfcontained.csproj'
$dist = Join-Path $projectRoot 'dist'
$packageCache = Join-Path $projectRoot '.tools\nuget-packages'
foreach ($arch in @('x64','x86')) {
    $output = Join-Path $dist ('cute-clash-self-contained-' + $arch)
    & $dotnet publish $project -c Release -r "win7-$arch" --self-contained true -p:BuildRuntimeTests=false -o $output --nologo
    if ($LASTEXITCODE -ne 0) { throw "Windows publish failed: $arch" }
    New-Item -ItemType Directory -Path (Join-Path $output 'core') -Force | Out-Null
    Copy-Item -Path (Join-Path $projectRoot "dependencies\mihomo\$arch\*") -Destination (Join-Path $output 'core') -Force
    foreach ($folder in @('licenses','examples','docs','assets')) { Copy-Item -LiteralPath (Join-Path $projectRoot $folder) -Destination $output -Recurse -Force }
    foreach ($file in @('README.md','README.en.md','CHANGELOG.md','LICENSE','dependencies.lock.json','runtime.lock.json')) { Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $output -Force }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'installer\NSIS-LICENSE.txt') -Destination (Join-Path $output 'licenses') -Force
    $runtimeLicenseDirectory = Join-Path $output 'licenses\dotnet'
    New-Item -ItemType Directory -Path $runtimeLicenseDirectory -Force | Out-Null
    $runtimePackages = @()
    foreach ($component in @('microsoft.netcore.app.runtime','microsoft.windowsdesktop.app.runtime')) {
        $id = "$component.win-$arch"
        $componentDirectory = Join-Path $packageCache ($id + '\' + $runtimeLock.runtimeVersion)
        $noticeFiles = @(Get-ChildItem -LiteralPath $componentDirectory -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY|ThirdParty)' })
        if (!($noticeFiles | Where-Object { $_.Name -match '^LICENSE' })) { throw "Missing original license for $id" }
        if ($component -eq 'microsoft.netcore.app.runtime' -and $noticeFiles.Count -lt 2) { throw "Missing original notices for $id" }
        foreach ($notice in $noticeFiles) { Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $runtimeLicenseDirectory ($id + '-' + $notice.Name)) -Force }
        $packageHash = (Get-Content -Raw -LiteralPath (Join-Path $componentDirectory ($id + '.' + $runtimeLock.runtimeVersion + '.nupkg.sha512'))).Trim()
        $runtimePackages += [pscustomobject]@{ id=$id; version=$runtimeLock.runtimeVersion; nugetSha512Base64=$packageHash }
    }
    $runtimePackages | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runtimeLicenseDirectory 'packages.json') -Encoding UTF8
    foreach ($required in @('cute-clash.exe','cute-clash.dll','coreclr.dll','hostfxr.dll','hostpolicy.dll','System.Windows.Forms.dll','ucrtbase.dll','vcruntime140_cor3.dll','api-ms-win-crt-runtime-l1-1-0.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $output $required))) { throw "Incomplete bundled runtime: $arch/$required" }
    }
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $output 'cute-clash.runtimeconfig.json') -Raw | ConvertFrom-Json
    if ($runtimeConfig.runtimeOptions.framework -or $runtimeConfig.runtimeOptions.frameworks) { throw 'Publish unexpectedly requires an installed .NET runtime.' }
    foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
        if ($framework.version -ne $runtimeLock.runtimeVersion) { throw 'Unexpected bundled runtime version.' }
    }
    if ($RunTests) {
        $testOutput = Join-Path $projectRoot "artifacts\runtime-tests\$arch"
        & $dotnet publish $project -c Release -r "win7-$arch" --self-contained true -p:BuildRuntimeTests=true -o $testOutput --nologo
        if ($LASTEXITCODE -ne 0) { throw "Bundled-runtime test compilation failed: $arch" }
        & (Join-Path $testOutput 'cute-clash-runtime-tests.exe') $projectRoot
        if ($LASTEXITCODE -ne 0) { throw "Bundled-runtime tests failed: $arch" }
        Copy-Item -LiteralPath (Join-Path $projectRoot "artifacts\tests\result-$arch.txt") -Destination (Join-Path $testOutput 'result.txt') -Force
    }
}
if ($Package) {
    foreach ($arch in @('x64','x86')) {
        $zip = Join-Path $dist "cute-clash-$version-windows-$arch.zip"
        if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
        $portableRoot = Join-Path $dist "cute-clash-self-contained-$arch"
        $archive = [IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in Get-ChildItem -LiteralPath $portableRoot -File -Recurse) {
                $entry = 'cute-clash/' + $file.FullName.Substring($portableRoot.Length + 1).Replace('\','/')
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$entry,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        } finally { $archive.Dispose() }
        Write-Output "Packaged $zip"
    }
}
if ($Installer) { & (Join-Path $PSScriptRoot 'build-installer.ps1') -Version $version; if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' } }
if ($Package -or $Installer) {
    Get-ChildItem -LiteralPath $dist -File | Where-Object { $_.Name -like "cute-clash-$version-windows-*" } | Sort-Object Name | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    } | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII
}
