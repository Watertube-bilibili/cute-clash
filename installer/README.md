# Cute Clash installer

The normal Windows installer includes the application's private .NET runtime. It does not install .NET Framework, write a shared .NET installation, modify Windows components, start the proxy, or enable TUN. It uses the approved `assets/cute-clash.ico` without changing the app's icon or interface.

## Build

Prepare the complete self-contained payloads in `dist/cute-clash-self-contained-x64` and `dist/cute-clash-self-contained-x86`, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

Outputs are `dist/cute-clash-0.3.0-windows-x64-setup.exe` and `dist/cute-clash-0.3.0-windows-x86-setup.exe`. `-Architecture x64` / `x86`, `-Version`, `-PayloadRoot`, and `-OutputRoot` are also supported. With a custom `-PayloadRoot`, select one architecture at a time.

The build downloads NSIS 3.12, verifies its pinned SHA-256 against the value recorded from [the official SourceForge file metadata](https://sourceforge.net/projects/nsis/files/NSIS%203/3.12/), and extracts it locally in `.tools/nsis`. A mirror is accepted only if it produces the same pinned bytes. The installer itself is native Unicode Win32 code and requires no .NET runtime to launch.

NSIS license text is preserved in `NSIS-LICENSE.txt`; its upstream source is available as `nsis-3.12-src.tar.bz2` on the same official release page. The setup script, compiler inputs, generated-file-list logic, and pinned toolchain are included in this repository.

## Behavior and boundaries

- Supports Windows 7 SP1 and later, including Windows 10 and Windows 11. The x64 package rejects 32-bit Windows; the x86 package works on 32-bit and 64-bit Windows.
- English and Simplified Chinese setup, per-user installation in `%LOCALAPPDATA%\Programs\cute-clash`, Start menu shortcuts, an optional desktop shortcut, and a per-user uninstall entry. No administrator prompt during setup.
- Registers `clash://` only when its component is selected. If another program already handles these links, this component starts unchecked. Selecting it explicitly replaces the previous handler. Uninstall removes the registration only while its command still points exactly to this installation.
- Checks the secure DLL loader APIs supplied by KB3063858 or superseding Windows updates. A package containing `ucrtbase.dll` and the full UCRT API-set payload uses its bundled UCRT; otherwise setup checks the architecture-appropriate system UCRT and explains how to install it. It never silently installs a Windows update. The [Microsoft prerequisites](https://learn.microsoft.com/dotnet/core/install/windows#dependencies) still apply to unpatched Windows installations.
- Refuses system/data folders, local drive roots, network/device paths, existing nonempty folders without a matching installation marker, and any reparse point in the target's ancestry or bundled paths. Existing copies must be upgraded in place, or uninstalled before choosing another directory or architecture.
- Refuses to continue while `cute-clash.exe` or `win7-clash.exe` is running. An exclusive, non-truncating write-open check also detects a locked application or core executable. It does not kill the app or rewrite proxy settings.
- Generates a literal uninstall file list from the exact build payload. Uninstall uses individual `Delete` operations and nonrecursive `RMDir` operations; it never recursively deletes an installation directory, removes user data, or deletes files added by the user. It checks the product marker and registered installation path before removing anything. Incomplete file removal keeps the uninstall entry available for another attempt.

## Verification

Compilation checks both the NSIS script and generated file lists. Before release, run installation tests in an isolated Windows account or VM: a successful silent install, upgrade-in-place, nonempty/dangerous directory refusal, application-running refusal, nested junction refusal, and uninstall with extra files present. Confirm other applications' `clash://` handlers survive an unchecked component and survive uninstall after another program takes over.

```powershell
# Use an isolated test account/VM. /D= must be the final argument, without quotes.
Start-Process .\dist\cute-clash-0.3.0-windows-x64-setup.exe -ArgumentList '/S /D=C:\Temp\cute-clash-installer-test' -Wait -PassThru
Start-Process C:\Temp\cute-clash-installer-test\uninstall.exe -ArgumentList '/S' -Wait -PassThru
```

Silent errors: `1633` unsupported OS/architecture/prerequisite, `1639` unsafe or mismatched installation path, `1618` application or installer already running, `5` extraction/removal failure. Silent setup never launches the app. Interactive setup offers an unchecked launch option on its final page.

Builds and tests on Windows 10/11 do not establish Windows 7 SP1 or real TUN compatibility. Record actual Win7 and TUN results separately.
