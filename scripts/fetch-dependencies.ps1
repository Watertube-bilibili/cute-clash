#Requires -Version 3.0
[CmdletBinding()]
param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$rootPrefix = $projectRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$manifestPath = Join-Path $projectRoot 'dependencies.lock.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) { throw 'Unsupported dependency lock format.' }

function Resolve-ProjectPath([string]$relativePath) {
    if ([string]::IsNullOrWhiteSpace($relativePath) -or [IO.Path]::IsPathRooted($relativePath)) {
        throw "Dependency path must be relative: $relativePath"
    }
    $resolvedPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $relativePath))
    if (-not $resolvedPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Dependency path escapes the project directory: $relativePath"
    }
    return $resolvedPath
}

function Get-Sha256([string]$path) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $inputStream = [IO.File]::OpenRead($path)
    try {
        return [BitConverter]::ToString($algorithm.ComputeHash($inputStream)).Replace('-', '').ToLowerInvariant()
    } finally { $inputStream.Dispose(); $algorithm.Dispose() }
}

function Assert-Hash([string]$path, [string]$expected) {
    if ($expected -notmatch '^[a-fA-F0-9]{64}$') { throw "Missing or invalid pinned SHA256: $path" }
    if (-not [IO.File]::Exists($path)) { throw "Missing dependency: $path" }
    $actual = Get-Sha256 $path
    if ($actual -ne $expected.ToLowerInvariant()) {
        throw "SHA256 mismatch; refusing to use $path. Expected $expected; got $actual. Remove the invalid file and retry."
    }
}

function Ensure-Parent([string]$path) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
}

if ($VerifyOnly) {
    foreach ($artifact in $manifest.artifacts) {
        Assert-Hash (Resolve-ProjectPath $artifact.path) $artifact.sha256
        foreach ($output in $artifact.outputs) {
            Assert-Hash (Resolve-ProjectPath $output.path) $output.sha256
        }
    }
    Write-Host 'All pinned dependency archives, licenses, and extracted files passed SHA256 verification.'
    exit 0
}

# Use the OS certificate store and ordinary HTTPS verification. No insecure fallback.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'

# Verify every download before extracting or replacing any executable/library.
foreach ($artifact in $manifest.artifacts) {
    $downloadPath = Resolve-ProjectPath $artifact.path
    if (-not [IO.File]::Exists($downloadPath)) {
        $uri = [Uri]$artifact.url
        if ($uri.Scheme -ne 'https') { throw "Only HTTPS dependency URLs are allowed: $uri" }
        Ensure-Parent $downloadPath
        $pendingDownload = $downloadPath + '.download-' + [Guid]::NewGuid().ToString('N')
        try {
            Write-Host ("Downloading pinned {0} {1}..." -f $artifact.name, $artifact.version)
            Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $pendingDownload
            Assert-Hash $pendingDownload $artifact.sha256
            [IO.File]::Move($pendingDownload, $downloadPath)
        } finally {
            if ([IO.File]::Exists($pendingDownload)) { [IO.File]::Delete($pendingDownload) }
        }
    }
    Assert-Hash $downloadPath $artifact.sha256
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($artifact in $manifest.artifacts) {
    if (@($artifact.outputs).Count -eq 0) { continue }
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-ProjectPath $artifact.path))
    try {
        foreach ($output in $artifact.outputs) {
            $destination = Resolve-ProjectPath $output.path
            if ([IO.File]::Exists($destination) -and (Get-Sha256 $destination) -eq $output.sha256) { continue }
            $entry = $archive.GetEntry($output.entry)
            if ($null -eq $entry) { throw "Pinned entry not found: $($output.entry)" }
            Ensure-Parent $destination
            $pendingOutput = $destination + '.extract-' + [Guid]::NewGuid().ToString('N')
            try {
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $pendingOutput, $false)
                Assert-Hash $pendingOutput $output.sha256
                if ([IO.File]::Exists($destination)) {
                    [IO.File]::Replace($pendingOutput, $destination, $null)
                } else {
                    [IO.File]::Move($pendingOutput, $destination)
                }
            } finally {
                if ([IO.File]::Exists($pendingOutput)) { [IO.File]::Delete($pendingOutput) }
            }
        }
    } finally { $archive.Dispose() }
}
Write-Host 'Pinned dependencies ready. No driver, system proxy, or system compatibility layer was installed.'
