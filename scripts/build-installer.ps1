#requires -Version 5.1
param(
    [ValidateSet('x64', 'x86', 'all')][string]$Architecture = 'all',
    [string]$Version = '0.3.0',
    [string]$PayloadRoot,
    [string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must have three numeric components.' }
if (!$OutputRoot) { $OutputRoot = Join-Path $projectRoot 'dist' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$toolchain = Get-Content -Raw -LiteralPath (Join-Path $projectRoot 'installer\toolchain.lock.json') | ConvertFrom-Json
$toolRoot = Join-Path $projectRoot '.tools\nsis'
$archive = Join-Path $toolRoot $toolchain.archive
New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $toolchain.sha256) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    foreach ($url in $toolchain.urls) {
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $archive
            if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -eq $toolchain.sha256) { break }
            Write-Warning 'NSIS mirror returned a file that did not match the pinned SHA-256; trying the next mirror.'
        } catch { Write-Warning ('NSIS download failed: ' + $_.Exception.Message) }
    }
}
if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $toolchain.sha256) {
    throw 'NSIS archive did not match its pinned official SHA-256.'
}
# Always extract the verified archive, so an old or changed compiler is not reused.
Expand-Archive -LiteralPath $archive -DestinationPath $toolRoot -Force
$compiler = Join-Path $toolRoot ('nsis-' + $toolchain.version + '\makensis.exe')
function ConvertTo-NsisString([string]$Value) {
    if ($Value -match '["\r\n]') { throw 'Unsupported quotation mark or line break in a payload path.' }
    return $Value.Replace('$', '$$')
}
$architectures = @($Architecture)
if ($Architecture -eq 'all') { $architectures = @('x64', 'x86') }
foreach ($arch in $architectures) {
    $payload = $PayloadRoot
    if (!$payload) { $payload = Join-Path $projectRoot ('dist\cute-clash-self-contained-' + $arch) }
    $payload = [IO.Path]::GetFullPath($payload).TrimEnd('\')
    foreach ($required in @('cute-clash.exe', 'cute-clash.dll', 'cute-clash.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll', 'System.Windows.Forms.dll', 'core\mihomo.exe', 'core\wintun.dll', 'LICENSE')) {
        if (!(Test-Path -LiteralPath (Join-Path $payload $required) -PathType Leaf)) { throw "Incomplete $arch self-contained payload: missing $required" }
    }
    $allItems = @(Get-ChildItem -LiteralPath $payload -Force -Recurse)
    if (@($allItems | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }).Count -gt 0) { throw 'Payload must not contain symbolic links or junctions.' }
    $files = @($allItems | Where-Object { !$_.PSIsContainer } | Sort-Object FullName)
    if (@($files | Where-Object { $_.Name -in @('cute-clash-install.ini', 'uninstall.exe', 'settings.json', 'proxy-recovery.json', 'runtime.yaml') }).Count -gt 0) {
        throw 'Payload contains installer-owned metadata or user data.'
    }
    $buildFolder = Join-Path $projectRoot ('artifacts\installer\' + $arch)
    New-Item -ItemType Directory -Path $buildFolder -Force | Out-Null
    $installLines = New-Object 'Collections.Generic.List[string]'
    $uninstallLines = New-Object 'Collections.Generic.List[string]'
    $guardLines = New-Object 'Collections.Generic.List[string]'
    $manifest = New-Object 'Collections.Generic.List[string]'
    $currentDirectory = $null
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($payload.Length + 1)
        $directory = Split-Path -Parent $relative
        if ($currentDirectory -cne $directory) {
            $suffix = if ($directory) { '\' + (ConvertTo-NsisString $directory) } else { '' }
            $installLines.Add('SetOutPath "$INSTDIR' + $suffix + '"')
            $currentDirectory = $directory
        }
        $installLines.Add('ClearErrors')
        $installLines.Add('File "' + (ConvertTo-NsisString $file.FullName) + '"')
        $installLines.Add('${If} ${Errors}')
        $installLines.Add('    SetErrorLevel 5')
        $installLines.Add('    Abort')
        $installLines.Add('${EndIf}')
        $uninstallLines.Add('${If} ${FileExists} "$INSTDIR\' + (ConvertTo-NsisString $relative) + '"')
        $uninstallLines.Add('    ClearErrors')
        $uninstallLines.Add('    Delete "$INSTDIR\' + (ConvertTo-NsisString $relative) + '"')
        $uninstallLines.Add('    ${If} ${Errors}')
        $uninstallLines.Add('        StrCpy $RemovalFailed 1')
        $uninstallLines.Add('    ${EndIf}')
        $uninstallLines.Add('${EndIf}')
        $manifest.Add($relative)
    }
    foreach ($directory in @($allItems | Where-Object { $_.PSIsContainer } | Sort-Object { $_.FullName.Length } -Descending)) {
        $relative = $directory.FullName.Substring($payload.Length + 1)
        $uninstallLines.Add('RMDir "$INSTDIR\' + (ConvertTo-NsisString $relative) + '"')
    }
    $guardPaths = @($allItems | ForEach-Object { $_.FullName.Substring($payload.Length + 1) }) + @('cute-clash-install.ini', 'uninstall.exe')
    foreach ($relative in $guardPaths) {
        $guardLines.Add('System::Call ''kernel32::GetFileAttributesW(w "$INSTDIR\' + (ConvertTo-NsisString $relative) + '") i .r0''')
        $guardLines.Add('${If} $0 != -1')
        $guardLines.Add('    IntOp $1 $0 & 0x400')
        $guardLines.Add('    ${If} $1 != 0')
        $guardLines.Add('        StrCpy $UnsafeReason 1')
        $guardLines.Add('        Return')
        $guardLines.Add('    ${EndIf}')
        $guardLines.Add('${EndIf}')
    }
    $utf8 = New-Object Text.UTF8Encoding($true)
    $licenseHeader = @'
Cute Clash - licenses / 许可说明

The Cute Clash application source is GPL-3.0-or-later. The unchanged Microsoft .NET runtime libraries and Wintun driver are separate components distributed under their respective licenses below. The application's GPL does not relicense these third-party binaries.

Cute Clash 应用源码采用 GPL-3.0-or-later。未经修改的 Microsoft .NET 运行库和 Wintun 驱动分别按下列原始许可分发，应用的 GPL 不会更改这些第三方二进制的许可。

For additional component notices, see the installed licenses directory.
其他组件声明见安装目录中的 licenses 文件夹。

'@
    $combinedLicense = $licenseHeader + "`r`n===== Cute Clash: GNU GPL v3 =====`r`n`r`n" + [IO.File]::ReadAllText((Join-Path $projectRoot 'LICENSE'))
    foreach ($licensePart in @(@('Microsoft .NET Library License', 'dotnet-Library-LICENSE.txt'), @('Wintun binary license', 'wintun-binary-LICENSE.txt'))) {
        $licenseFile = Join-Path $projectRoot ('licenses\' + $licensePart[1])
        if (!(Test-Path -LiteralPath $licenseFile)) { throw ('Missing redistribution license: ' + $licensePart[1]) }
        $combinedLicense += "`r`n`r`n===== " + $licensePart[0] + " =====`r`n`r`n" + [IO.File]::ReadAllText($licenseFile)
    }
    [IO.File]::WriteAllText((Join-Path $buildFolder 'combined-license.txt'), $combinedLicense, $utf8)
    [IO.File]::WriteAllLines((Join-Path $buildFolder 'install-files.nsh'), $installLines, $utf8)
    [IO.File]::WriteAllLines((Join-Path $buildFolder 'uninstall-files.nsh'), $uninstallLines, $utf8)
    [IO.File]::WriteAllLines((Join-Path $buildFolder 'guard-paths.nsh'), $guardLines, $utf8)
    [IO.File]::WriteAllLines((Join-Path $buildFolder 'payload-files.txt'), $manifest, $utf8)
    $output = Join-Path $OutputRoot "cute-clash-$Version-windows-$arch-setup.exe"
    $size = [Math]::Ceiling(($files | Measure-Object -Property Length -Sum).Sum / 1024)
    $extraDefines = @()
    $ucrtNames = @('ucrtbase.dll') + @('conio','convert','environment','filesystem','heap','locale','math','multibyte','private','process','runtime','stdio','string','time','utility' | ForEach-Object { "api-ms-win-crt-$_-l1-1-0.dll" })
    $missingUcrt = @($ucrtNames | Where-Object { !(Test-Path -LiteralPath (Join-Path $payload $_) -PathType Leaf) })
    if ($missingUcrt.Count -eq 0) { $extraDefines += '/DBUNDLED_UCRT=1' }
    & $compiler /NOCONFIG /INPUTCHARSET UTF8 /V3 "/DPROJECT_ROOT=$projectRoot" "/DBUILD_ROOT=$buildFolder" "/DOUTPUT_FILE=$output" "/DPRODUCT_VERSION=$Version" "/DPRODUCT_ARCH=$arch" "/DESTIMATED_SIZE=$size" @extraDefines (Join-Path $projectRoot 'installer\cute-clash.nsi')
    if ($LASTEXITCODE -ne 0) { throw "NSIS compilation failed for $arch." }
    Get-FileHash -LiteralPath $output -Algorithm SHA256 | Select-Object Path, Hash
}
