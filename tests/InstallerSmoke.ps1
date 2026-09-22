#requires -Version 5.1
param([switch]$Run, [string]$Version = '0.3.0')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\installer-smoke'))
$uninstallPath = 'Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\cute-clash'
$protocolPath = 'Registry::HKEY_CURRENT_USER\Software\Classes\clash'
$protocolCommandPath = $protocolPath + '\shell\open\command'
$startMenuPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Cute Clash'
$desktopPath = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Cute Clash.lnk'
if (!$Run) {
    Write-Output 'Pass -Run to install and uninstall in artifacts/installer-smoke/x64 and x86. This temporarily changes the current-user clash:// registration, which is backed up and restored in finally.'
    return
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid version.' }
if (Test-Path -LiteralPath $uninstallPath) { throw 'An existing Cute Clash uninstall entry must not be overwritten by this test.' }
if ((Test-Path -LiteralPath $startMenuPath) -or (Test-Path -LiteralPath $desktopPath)) { throw 'Existing user shortcuts must not be overwritten by this test.' }
if (@(Get-Process -Name cute-clash,win7-clash -ErrorAction SilentlyContinue).Count -gt 0) { throw 'Existing Cute Clash processes must not be interrupted by this test.' }
foreach ($arch in @('x64','x86')) {
    $setup = Join-Path $projectRoot "dist\cute-clash-$Version-windows-$arch-setup.exe"
    if (!(Test-Path -LiteralPath $setup)) { throw "Missing $arch installer." }
    $target = Join-Path $testRoot $arch
    if ((Test-Path -LiteralPath $target) -and @(Get-ChildItem -LiteralPath $target -Force).Count -gt 0) {
        throw "Test target is not empty: $target. Preserve or move previous test evidence before rerunning."
    }
}
$ancestorBeforeWrite = $testRoot
while ($ancestorBeforeWrite.Length -gt 3) {
    if ((Test-Path -LiteralPath $ancestorBeforeWrite) -and (((Get-Item -LiteralPath $ancestorBeforeWrite -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw 'The installer test workspace must not have a symbolic link or junction in its ancestry.'
    }
    $ancestorBeforeWrite = Split-Path -Parent $ancestorBeforeWrite
}
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$logPath = Join-Path $testRoot 'results.jsonl'
$resultPath = Join-Path $testRoot 'summary.json'
$backupPath = Join-Path $testRoot 'clash-protocol-before.reg'
$regExe = Join-Path $env:WINDIR 'System32\reg.exe'
$protocolExisted = Test-Path -LiteralPath $protocolPath
$protocolBackedUp = $false
$protocolTouched = $false
$fixtureProcess = $null
$fileLock = $null
$checks = New-Object 'Collections.Generic.List[string]'
$success = $false
$currentTarget = $null

function Assert-Condition([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    $script:checks.Add($Message)
    Write-Output ('PASS ' + $Message)
}
function Assert-TestTarget([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if (!$full.StartsWith($testRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing an operation outside the installer test workspace.' }
    $ancestor = $full
    while ($ancestor.Length -gt 3) {
        if (Test-Path -LiteralPath $ancestor) {
            if (((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Installer test paths must not contain symbolic links or junctions.' }
        }
        $ancestor = Split-Path -Parent $ancestor
    }
    return $full
}
function Invoke-TestProcess([string]$Path, [string]$Arguments, [string]$Label) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    $watch.Stop()
    [pscustomobject]@{ action=$Label; exitCode=$process.ExitCode; seconds=[Math]::Round($watch.Elapsed.TotalSeconds,2) } |
        ConvertTo-Json -Compress | Add-Content -LiteralPath $logPath -Encoding UTF8
    return $process.ExitCode
}
function Set-TestProtocol([string]$Command) {
    $script:protocolTouched = $true
    New-Item -Path $protocolCommandPath -Force | Out-Null
    Set-Item -LiteralPath $protocolCommandPath -Value $Command
    New-ItemProperty -LiteralPath $protocolPath -Name 'URL Protocol' -Value '' -PropertyType String -Force | Out-Null
}
function Read-ProtocolCommand {
    if (!(Test-Path -LiteralPath $protocolCommandPath)) { return $null }
    return (Get-Item -LiteralPath $protocolCommandPath).GetValue('')
}
function Invoke-RegistryCommand([string[]]$Arguments, [string]$Label) {
    $output = Join-Path $testRoot ($Label + '.stdout.log')
    $errors = Join-Path $testRoot ($Label + '.stderr.log')
    $process = Start-Process -FilePath $regExe -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $output -RedirectStandardError $errors
    return $process.ExitCode
}
function Wait-Uninstalled {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ((Test-Path -LiteralPath $uninstallPath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    return !(Test-Path -LiteralPath $uninstallPath)
}
function Get-DataSnapshot {
    $items = @()
    foreach ($folder in @('cute-clash','win7-clash')) {
        $directory = Join-Path $env:LOCALAPPDATA $folder
        if (Test-Path -LiteralPath $directory) {
            foreach ($file in Get-ChildItem -LiteralPath $directory -File -Recurse -Force) {
                $items += $folder + '\' + $file.FullName.Substring($directory.Length + 1) + ':' + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            }
        } else { $items += $folder + ':ABSENT' }
    }
    return ($items | Sort-Object) -join "`n"
}

$dataBefore = Get-DataSnapshot
try {
    if ($protocolExisted) {
        $regExit = Invoke-RegistryCommand @('export', 'HKEY_CURRENT_USER\Software\Classes\clash', ('"' + $backupPath + '"'), '/y') 'registry-backup'
        if ($regExit -ne 0 -or !(Test-Path -LiteralPath $backupPath)) { throw 'Could not back up the current clash protocol. No installation was attempted.' }
    }
    $protocolBackedUp = $true

    # A separate harmless process proves setup does not overwrite a running app.
    $fixtureRoot = Assert-TestTarget (Join-Path $testRoot 'fixture')
    if ((Test-Path -LiteralPath $fixtureRoot) -and @(Get-ChildItem -LiteralPath $fixtureRoot -Force).Count -gt 0) { throw 'The inert process fixture directory must be empty before this test.' }
    New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
    $fixtureSource = Join-Path $fixtureRoot 'Sleep.cs'
    $fixtureExe = Join-Path $fixtureRoot 'cute-clash.exe'
    [IO.File]::WriteAllText($fixtureSource, 'internal static class Sleep { private static void Main() { System.Threading.Thread.Sleep(120000); } }', (New-Object Text.UTF8Encoding($false)))
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    & $compiler /nologo /target:winexe "/out:$fixtureExe" $fixtureSource *> (Join-Path $testRoot 'fixture-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile the inert process fixture.' }
    $fixtureProcess = Start-Process -FilePath $fixtureExe -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 300
    $x64Setup = Join-Path $projectRoot "dist\cute-clash-$Version-windows-x64-setup.exe"
    $x64Target = Assert-TestTarget (Join-Path $testRoot 'x64')
    $exit = Invoke-TestProcess $x64Setup "/S /D=$x64Target" 'running-app-refused'
    Assert-Condition ($exit -eq 1618) 'Running application prevents setup.'
    Assert-Condition (!(Test-Path -LiteralPath (Join-Path $x64Target 'cute-clash.exe'))) 'Running-app refusal writes no application payload.'
    Stop-Process -Id $fixtureProcess.Id -Force
    $fixtureProcess.WaitForExit()
    $fixtureProcess.Dispose()
    $fixtureProcess = $null

    foreach ($arch in @('x64','x86')) {
        $setup = Join-Path $projectRoot "dist\cute-clash-$Version-windows-$arch-setup.exe"
        $currentTarget = Assert-TestTarget (Join-Path $testRoot $arch)
        $payload = Join-Path $projectRoot ('dist\cute-clash-self-contained-' + $arch)
        $foreignCommand = '"C:\cute-clash-test-only\previous-handler.exe" "%1"'
        if ($arch -eq 'x64') { Set-TestProtocol $foreignCommand } else { Set-TestProtocol '' }
        $exit = Invoke-TestProcess $setup "/S /D=$currentTarget" ($arch + '-install')
        Assert-Condition ($exit -eq 0) ($arch + ': installer exits successfully.')
        Assert-Condition (Test-Path -LiteralPath (Join-Path $currentTarget 'uninstall.exe')) ($arch + ': native uninstaller exists.')
        Assert-Condition ((Get-ItemProperty -LiteralPath $uninstallPath).InstallLocation -eq $currentTarget) ($arch + ': uninstall entry points to the isolated target.')
        Assert-Condition (Test-Path -LiteralPath (Join-Path $startMenuPath 'Cute Clash.lnk')) ($arch + ': Start menu shortcut exists.')
        Assert-Condition (!(Test-Path -LiteralPath $desktopPath)) ($arch + ': optional desktop shortcut remains unchecked in silent setup.')
        $payloadFiles = @(Get-ChildItem -LiteralPath $payload -File -Recurse -Force)
        foreach ($file in $payloadFiles) {
            $relative = $file.FullName.Substring($payload.Length + 1)
            $installed = Join-Path $currentTarget $relative
            if (!(Test-Path -LiteralPath $installed) -or (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
                throw "$arch installed payload mismatch: $relative"
            }
        }
        Assert-Condition ($payloadFiles.Count -gt 100) ($arch + ': every payload file matches its source SHA-256 (' + $payloadFiles.Count + ' files).')
        foreach ($runtimeFile in @('coreclr.dll','hostfxr.dll','vcruntime140_cor3.dll','ucrtbase.dll','api-ms-win-crt-runtime-l1-1-0.dll','System.Windows.Forms.dll')) {
            if (!(Test-Path -LiteralPath (Join-Path $currentTarget $runtimeFile))) { throw "Missing bundled runtime: $runtimeFile" }
        }
        Assert-Condition $true ($arch + ': private .NET, VC runtime, and UCRT are installed.')
        $expectedCommand = '"' + (Join-Path $currentTarget 'cute-clash.exe') + '" "%1"'
        if ($arch -eq 'x64') {
            Assert-Condition ((Read-ProtocolCommand) -ceq $foreignCommand) 'Existing clash:// handler is not replaced by default.'
        } else {
            Assert-Condition ((Read-ProtocolCommand) -ceq $expectedCommand) 'Registered clash:// command quotes both executable and URI.'
        }

        $corePath = Join-Path $currentTarget 'core\mihomo.exe'
        $coreHash = (Get-FileHash -LiteralPath $corePath -Algorithm SHA256).Hash
        $fileLock = [IO.File]::Open($corePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try {
            $exit = Invoke-TestProcess $setup "/S /D=$currentTarget" ($arch + '-locked-core-refused')
            Assert-Condition ($exit -eq 1618) ($arch + ': locked core prevents overwrite.')
        } finally { $fileLock.Dispose(); $fileLock = $null }
        Assert-Condition ((Get-FileHash -LiteralPath $corePath -Algorithm SHA256).Hash -eq $coreHash) ($arch + ': locked-core refusal preserves its bytes.')

        $exit = Invoke-TestProcess $setup "/S /D=$currentTarget" ($arch + '-upgrade')
        Assert-Condition ($exit -eq 0) ($arch + ': same-architecture upgrade succeeds in place.')
        $keepPath = Join-Path $currentTarget 'keep.txt'
        $keepContent = 'User-added installer smoke-test sentinel. Must survive uninstall.'
        [IO.File]::WriteAllText($keepPath, $keepContent)
        $successor = '"C:\cute-clash-test-only\next-handler.exe" "%1"'
        if ($arch -eq 'x64') { Set-TestProtocol $successor }
        $exit = Invoke-TestProcess (Join-Path $currentTarget 'uninstall.exe') '/S' ($arch + '-uninstall')
        Assert-Condition ($exit -eq 0 -and (Wait-Uninstalled)) ($arch + ': uninstall completes and removes its entry.')
        Assert-Condition ((Test-Path -LiteralPath $keepPath) -and [IO.File]::ReadAllText($keepPath) -ceq $keepContent) ($arch + ': uninstall preserves user-added files.')
        foreach ($file in $payloadFiles) {
            $relative = $file.FullName.Substring($payload.Length + 1)
            if (Test-Path -LiteralPath (Join-Path $currentTarget $relative)) { throw "$arch payload file survived uninstall: $relative" }
        }
        Assert-Condition $true ($arch + ': uninstall removes every owned payload file.')
        Assert-Condition (!(Test-Path -LiteralPath $startMenuPath) -and !(Test-Path -LiteralPath $desktopPath)) ($arch + ': uninstall removes its shortcuts.')
        if ($arch -eq 'x64') {
            Assert-Condition ((Read-ProtocolCommand) -ceq $successor) 'Uninstall preserves a successor clash:// handler.'
        } else {
            Assert-Condition (!(Test-Path -LiteralPath $protocolPath)) 'Uninstall removes its own clash:// handler.'
        }
        $exit = Invoke-TestProcess $setup "/S /D=$currentTarget" ($arch + '-nonempty-refused')
        Assert-Condition ($exit -eq 1639) ($arch + ': unmarked nonempty target is rejected.')
        Assert-Condition ([IO.File]::ReadAllText($keepPath) -ceq $keepContent) ($arch + ': rejected setup preserves existing files.')
        $currentTarget = $null
    }
    Assert-Condition ((Get-DataSnapshot) -ceq $dataBefore) 'Current and legacy application data remain byte-for-byte unchanged.'
    $success = $true
} finally {
    $cleanupError = $null
    if ($fileLock) { $fileLock.Dispose() }
    if ($fixtureProcess) {
        if (!$fixtureProcess.HasExited) { Stop-Process -Id $fixtureProcess.Id -Force; $fixtureProcess.WaitForExit() }
        $fixtureProcess.Dispose()
    }
    # On an assertion failure, clean up only the installation created by this run.
    try {
        if (Test-Path -LiteralPath $uninstallPath) {
            $registered = (Get-ItemProperty -LiteralPath $uninstallPath).InstallLocation
            $safeRegistered = Assert-TestTarget $registered
            $uninstaller = Join-Path $safeRegistered 'uninstall.exe'
            if (Test-Path -LiteralPath $uninstaller) {
                $null = Invoke-TestProcess $uninstaller '/S' 'cleanup-uninstall'
                if (!(Wait-Uninstalled)) { throw 'Test installation cleanup failed. Inspect the isolated test target.' }
            } else { throw 'Test installation has no uninstaller; manual cleanup is required.' }
        }
    } catch { $cleanupError = $_ }
    if ($protocolBackedUp -and $protocolTouched) {
        if (Test-Path -LiteralPath $protocolPath) { Remove-Item -LiteralPath $protocolPath -Recurse -Force }
        if ($protocolExisted) {
            $regExit = Invoke-RegistryCommand @('import', ('"' + $backupPath + '"')) 'registry-restore'
            if ($regExit -ne 0) { throw 'Restoring the original protocol registration failed; retain its local backup.' }
            $afterBackup = Join-Path $testRoot 'clash-protocol-after.reg'
            $regExit = Invoke-RegistryCommand @('export', 'HKEY_CURRENT_USER\Software\Classes\clash', ('"' + $afterBackup + '"'), '/y') 'registry-verify'
            if ($regExit -ne 0 -or (Get-FileHash -LiteralPath $afterBackup -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash) { throw 'Restored protocol registry does not match its backup.' }
        } elseif (Test-Path -LiteralPath $protocolPath) { throw 'Test protocol key remained after cleanup.' }
    }
    Assert-Condition (!(Test-Path -LiteralPath $uninstallPath) -and !(Test-Path -LiteralPath $startMenuPath) -and !(Test-Path -LiteralPath $desktopPath)) 'No test uninstall entries or shortcuts remain.'
    [pscustomobject]@{ passed=$success; checks=$checks.Count; assertions=@($checks); originalProtocolRestored=$protocolBackedUp; timeUtc=[DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resultPath -Encoding UTF8
    if ($cleanupError) { throw $cleanupError }
}
Write-Output ('Installer smoke tests completed: ' + $checks.Count + ' assertions.')
