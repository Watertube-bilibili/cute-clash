#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-StrictMode -Version Latest
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workRoot = Join-Path $projectRoot 'artifacts/mihomo-source'
$downloads = Join-Path $workRoot 'downloads'
$runRoot = Join-Path $workRoot ('run-' + [Guid]::NewGuid().ToString('N'))
$package = Join-Path $runRoot 'mihomo-v1.19.31-complete-source'
$sdkRoot = Join-Path $projectRoot '.tools/metacubex-go1.26'
$sdkArchive = Join-Path $projectRoot '.tools/metacubex-go1.26.windows-amd64.zip'
$go = Join-Path $sdkRoot 'go/bin/go.exe'
$source = @{
    name = 'mihomo-v1.19.31-source.zip'
    url = 'https://codeload.github.com/MetaCubeX/mihomo/zip/refs/tags/v1.19.31'
    sha256 = 'ef491b55a920449c5aa46d5903bbb7007839a1fcf6baaac277016945cbc3e79e'
    path = (Join-Path $projectRoot 'dependencies/sources/mihomo-v1.19.31-source.zip')
}
$vendor = @{
    name = 'vendor.tar.gz'
    url = 'https://github.com/MetaCubeX/mihomo/releases/download/v1.19.31/vendor.tar.gz'
    sha256 = '4089c4e51ee1ecff64845d4de6886e3ebd3adc20ee6cf4520ce76465a16b65db'
    path = (Join-Path $downloads 'vendor.tar.gz')
}
$toolchain = @{
    name = 'toolchain.tar.gz'
    url = 'https://github.com/MetaCubeX/mihomo/releases/download/v1.19.31/toolchain.tar.gz'
    sha256 = 'd7b43406ce2722a4206ae173ee972f17a2da02e55f234a95b7c17a1bb9dd7e10'
    path = (Join-Path $downloads 'toolchain.tar.gz')
}
$sdk = @{
    name = 'go1.26.windows-amd64.zip'
    url = 'https://github.com/MetaCubeX/go/releases/download/build/go1.26.windows-amd64.zip'
    sha256 = '15cf610d92dedd2b74b93c3af44154c450bee3102c99d726f05998116017749b'
    path = $sdkArchive
}

function Assert-Hash([string]$path, [string]$sha256) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing source material: $path" }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $sha256) { throw "SHA256 mismatch: $path" }
}
function Ensure-Download($item) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($item.path)) | Out-Null
    if (!(Test-Path -LiteralPath $item.path -PathType Leaf)) {
        $pending = $item.path + '.download'
        Write-Host ('Downloading ' + $item.name)
        Invoke-WebRequest -UseBasicParsing -Uri $item.url -OutFile $pending
        Assert-Hash $pending $item.sha256
        [IO.File]::Move($pending, $item.path)
    }
    Assert-Hash $item.path $item.sha256
}
function Write-Utf8([string]$path, [string]$text) {
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding($false)))
}
function Invoke-Go([string[]]$arguments, [string]$record) {
    # Windows PowerShell 5.1 wraps native stderr lines in ErrorRecord objects.
    # Go writes ordinary download progress there; judge failure by exit code.
    $priorPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = & $go @arguments 2>&1
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $priorPreference }
    $text = ($lines | ForEach-Object { $_.ToString() }) -join "`n"
    if ($record) { Write-Utf8 $record ($text + "`n") }
    if ($code -ne 0) { throw "go $($arguments -join ' ') failed: $text" }
    return $text
}

foreach ($item in @($source, $vendor, $toolchain, $sdk)) { Ensure-Download $item }
[IO.Directory]::CreateDirectory($package) | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (!(Test-Path -LiteralPath $go -PathType Leaf)) {
    [IO.Compression.ZipFile]::ExtractToDirectory($sdkArchive, $sdkRoot)
}
$goVersion = Invoke-Go @('version') (Join-Path $package 'preparation-go-version.txt')
if ($goVersion -ne 'go version go1.26.8 windows/amd64') { throw "Unexpected preparation toolchain: $goVersion" }

[IO.Compression.ZipFile]::ExtractToDirectory($source.path, $runRoot)
$sourceRoot = Join-Path $runRoot 'mihomo-1.19.31'
$originalGoModHash = (Get-FileHash -LiteralPath (Join-Path $sourceRoot 'go.mod') -Algorithm SHA256).Hash
$originalGoSumHash = (Get-FileHash -LiteralPath (Join-Path $sourceRoot 'go.sum') -Algorithm SHA256).Hash
$vendorRoot = Join-Path $runRoot 'official-vendor'
[IO.Directory]::CreateDirectory($vendorRoot) | Out-Null
& tar -xzf $vendor.path -C $vendorRoot
if ($LASTEXITCODE -ne 0) { throw 'Could not extract official vendor archive.' }
$toolchainVersion = (& tar -xOf $toolchain.path './go/VERSION') -join "`n"
if ($LASTEXITCODE -ne 0 -or !$toolchainVersion.StartsWith('go1.26.8')) { throw 'Unexpected packaged release toolchain VERSION.' }
$toolchainEntries = @(& tar -tzf $toolchain.path)
if ($LASTEXITCODE -ne 0 -or $toolchainEntries -notcontains './go/src/runtime/os_windows.go' -or $toolchainEntries -notcontains './go/LICENSE') {
    throw 'The release toolchain archive does not contain the expected source and license.'
}
Write-Utf8 (Join-Path $package 'release-toolchain-VERSION.txt') ($toolchainVersion + "`n")

# Isolate Go's writable cache and settings from the developer's configuration.
$savedEnvironment = @{}
foreach ($name in @('GOPATH','GOMODCACHE','GOCACHE','GOTOOLCHAIN','GOPROXY','GOSUMDB','GONOSUMDB','GONOPROXY','GOPRIVATE','GOFLAGS','GOENV','GOWORK','GOROOT')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    $env:GOPATH = Join-Path $workRoot 'go-work'
    $env:GOMODCACHE = Join-Path $workRoot 'module-cache'
    $env:GOCACHE = Join-Path $workRoot 'build-cache'
    $env:GOTOOLCHAIN = 'local'
    $env:GOPROXY = 'https://proxy.golang.org'
    $env:GOSUMDB = 'sum.golang.org'
    $env:GONOSUMDB = ''
    $env:GONOPROXY = ''
    $env:GOPRIVATE = ''
    $env:GOFLAGS = '-mod=readonly'
    $env:GOENV = 'off'
    $env:GOWORK = 'off'
    $env:GOROOT = Join-Path $sdkRoot 'go'
    Push-Location $sourceRoot
    try {
        Write-Host 'Downloading and checking the exact go.mod / go.sum modules.'
        Invoke-Go @('mod','download') (Join-Path $package 'go-mod-download.txt') | Out-Null
        Invoke-Go @('mod','verify') (Join-Path $package 'go-mod-verify.txt') | Out-Null
        $regenerated = Join-Path $runRoot 'regenerated-vendor'
        Invoke-Go @('mod','vendor','-o',$regenerated) (Join-Path $package 'go-mod-vendor.txt') | Out-Null
        Invoke-Go @('list','-m','-mod=readonly','all') (Join-Path $package 'modules.txt') | Out-Null
    } finally { Pop-Location }
} finally {
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}
Assert-Hash (Join-Path $sourceRoot 'go.mod') $originalGoModHash
Assert-Hash (Join-Path $sourceRoot 'go.sum') $originalGoSumHash

# Compare every vendored source/license/manifest byte against a regeneration
# whose module inputs Go has checked against the unchanged upstream go.sum.
$official = Join-Path $vendorRoot 'vendor'
$officialFiles = @(Get-ChildItem -LiteralPath $official -Recurse -File)
$generatedFiles = @(Get-ChildItem -LiteralPath $regenerated -Recurse -File)
if ($officialFiles.Count -ne $generatedFiles.Count) { throw 'Official and regenerated vendor file counts differ.' }
$vendorHashes = New-Object Collections.Generic.List[string]
foreach ($file in $officialFiles) {
    $relative = $file.FullName.Substring($official.Length + 1)
    $expected = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Hash (Join-Path $regenerated $relative) $expected
    $vendorHashes.Add($expected + '  vendor/' + $relative.Replace('\','/'))
}
Write-Utf8 (Join-Path $package 'vendor-SHA256SUMS.txt') (($vendorHashes | Sort-Object) -join "`n")
Write-Utf8 (Join-Path $package 'vendor-verification.txt') ("Official vendor.tar.gz and regenerated vendor match byte-for-byte: " + $officialFiles.Count + " files.`nOriginal go.mod and go.sum were unchanged. All downloaded modules passed go mod verify.`n")

$coreHashes = @{
    x64 = '9f2ad8968022eae9b972c01a87de1a4dc19c949834db899d732bc0129b103478'
    x86 = '05fd0c26ee7a60a4f8d86ce0b59212a802eca509b73b0096148eece9e066a971'
}
foreach ($architecture in @('x64','x86')) {
    $core = Join-Path $projectRoot ('dependencies/mihomo/' + $architecture + '/mihomo.exe')
    Assert-Hash $core $coreHashes[$architecture]
    $info = Invoke-Go @('version','-m',$core) $null
    if (!$info.Contains('go1.26.8') -or !$info.Contains('vcs.revision=ab405bad5beeeac8b003bb01f60f134f6df54471') -or !$info.Contains('-tags=with_gvisor')) {
        throw 'The core build information does not match the expected release.'
    }
    # The first line contains only the input path and compiler version. Avoid
    # publishing a preparation-machine-specific absolute filesystem path.
    $info = $info.Replace($core, 'mihomo-' + $architecture + '.exe')
    Write-Utf8 (Join-Path $package ('core-build-info-' + $architecture + '.txt')) ($info + "`n")
    $runtimeVersion = (& $core -v) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect core version.' }
    Write-Utf8 (Join-Path $package ('core-version-' + $architecture + '.txt')) ($runtimeVersion + "`n")
}

foreach ($item in @($source,$vendor,$toolchain)) { Copy-Item -LiteralPath $item.path -Destination (Join-Path $package $item.name) }
Copy-Item -LiteralPath (Join-Path $sourceRoot '.github/workflows/build.yml') -Destination (Join-Path $package 'upstream-build.yml')
Copy-Item -LiteralPath (Join-Path $sourceRoot 'go.mod'),(Join-Path $sourceRoot 'go.sum'),(Join-Path $sourceRoot 'LICENSE') -Destination $package
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/MIHOMO-SOURCE.md') -Destination (Join-Path $package 'README.md')
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $package 'prepare-mihomo-source.ps1')
$manifest = [ordered]@{
    version = 'v1.19.31'
    sourceCommit = 'ab405bad5beeeac8b003bb01f60f134f6df54471'
    preparedAtUtc = [DateTime]::UtcNow.ToString('O')
    toolchainVersion = 'go1.26.8'
    toolchainSource = 'MetaCubeX/mihomo v1.19.31 official release toolchain.tar.gz: includes patched Go source, license and Linux amd64 compiler binaries'
    preparationToolchain = @{ url = $sdk.url; sha256 = $sdk.sha256; version = $goVersion }
    inspectedCoreSHA256 = $coreHashes
    sourceMaterials = @(@{ name = $source.name; url = $source.url; sha256 = $source.sha256 }, @{ name = $vendor.name; url = $vendor.url; sha256 = $vendor.sha256 }, @{ name = $toolchain.name; url = $toolchain.url; sha256 = $toolchain.sha256 })
    artifactHashProvenance = 'Release asset digests from GitHub API for MetaCubeX/mihomo v1.19.31 and MetaCubeX/go build; tag archive digest pinned in dependencies.lock.json'
    vendorVerification = @{ matchedFiles = $officialFiles.Count; regeneratedFromGoSum = $true; goModUnchanged = $true; goSumUnchanged = $true }
    binaryRebuilt = $false
    byteReproducibilityClaimed = $false
}
Write-Utf8 (Join-Path $package 'SOURCE-MANIFEST.json') ($manifest | ConvertTo-Json -Depth 8)
$checksums = @(Get-ChildItem -LiteralPath $package -File | Sort-Object Name | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name })
Write-Utf8 (Join-Path $package 'SHA256SUMS.txt') (($checksums -join "`n") + "`n")
$dist = Join-Path $projectRoot 'dist'
[IO.Directory]::CreateDirectory($dist) | Out-Null
$output = Join-Path $dist 'mihomo-v1.19.31-complete-source.zip'
$pendingZip = Join-Path $runRoot 'complete-source.zip'
# .NET Framework's CreateFromDirectory can write backslashes to ZIP names.
# Explicit POSIX separators let the same source package unpack on Linux.
$outputArchive = [IO.Compression.ZipFile]::Open($pendingZip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $package -Recurse -File) {
        $entryName = 'mihomo-v1.19.31-complete-source/' + $file.FullName.Substring($package.Length + 1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($outputArchive, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $outputArchive.Dispose() }
if (Test-Path -LiteralPath $output) {
    # PowerShell 5.1 converts a null string argument to an empty string. Give
    # File.Replace a concrete backup path so repeated preparation also works.
    [IO.File]::Replace($pendingZip, $output, (Join-Path $runRoot 'previous-complete-source.zip'))
} else { [IO.File]::Move($pendingZip, $output) }
Write-Host ("Source materials ready: {0}; vendor files checked: {1}" -f $output, $officialFiles.Count)
Get-FileHash -LiteralPath $output -Algorithm SHA256
