; -*- coding: utf-8 -*-
Unicode true
ManifestSupportedOS all
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
SetDatablockOptimize on
CRCCheck on
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "WinVer.nsh"
!include "x64.nsh"

!define PRODUCT_ID "{FCBA084D-867A-4A32-A502-EF5D0E7AECC3}"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\cute-clash"
Name "Cute Clash ${PRODUCT_VERSION} (${PRODUCT_ARCH})"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\cute-clash"
InstallDirRegKey HKCU "${UNINSTALL_KEY}" "InstallLocation"
BrandingText "Cute Clash"
Icon "${PROJECT_ROOT}\assets\cute-clash.ico"
UninstallIcon "${PROJECT_ROOT}\assets\cute-clash.ico"
VIProductVersion "${PRODUCT_VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "Cute Clash"
VIAddVersionKey /LANG=1033 "FileDescription" "Cute Clash Windows installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Cute Clash contributors"
ShowInstDetails show
ShowUninstDetails show

Var SetupMutex
Var UnsafeReason
Var TargetPath
Var ProcessSnapshot
Var ProcessBuffer
Var AppRunning
Var RemovalFailed

!define MUI_ICON "${PROJECT_ROOT}\assets\cute-clash.ico"
!define MUI_UNICON "${PROJECT_ROOT}\assets\cute-clash.ico"
!define MUI_ABORTWARNING
!define MUI_LANGDLL_ALLLANGUAGES
!define MUI_LANGDLL_REGISTRY_ROOT HKCU
!define MUI_LANGDLL_REGISTRY_KEY "${UNINSTALL_KEY}"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"
!define MUI_WELCOMEPAGE_TEXT "$(WelcomeText)"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${BUILD_ROOT}\combined-license.txt"
!insertmacro MUI_PAGE_COMPONENTS
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE DirectoryLeave
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\cute-clash.exe"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_RESERVEFILE_LANGDLL

LangString WelcomeText ${LANG_ENGLISH} "This package includes the .NET runtime. You do not need to install .NET Framework or a separate .NET Desktop Runtime.$\r$\n$\r$\nFor Windows 7 SP1, Windows 10, Windows 11, and later. Windows 7 needs its secure DLL loader updates.$\r$\n$\r$\nInstalls for your current Windows account. Administrator permission is requested only when you enable TUN in the app."
LangString WelcomeText ${LANG_SIMPCHINESE} "此安装包已包含 .NET 运行时，无需另装 .NET Framework 或 .NET 桌面运行时。$\r$\n$\r$\n支持 Windows 7 SP1、Windows 10、Windows 11 及以上系统。Windows 7 需要具备安全 DLL 加载器补丁。$\r$\n$\r$\n软件安装到当前 Windows 用户目录。启用 TUN 时才需要管理员权限。"
LangString MainSection ${LANG_ENGLISH} "Application and bundled .NET runtime (required)"
LangString MainSection ${LANG_SIMPCHINESE} "应用和内置 .NET 运行时（必选）"
LangString DesktopSection ${LANG_ENGLISH} "Desktop shortcut"
LangString DesktopSection ${LANG_SIMPCHINESE} "桌面快捷方式"
LangString ProtocolSection ${LANG_ENGLISH} "Handle clash:// subscription links"
LangString ProtocolSection ${LANG_SIMPCHINESE} "处理 clash:// 订阅链接"
LangString ProtocolDescription ${LANG_ENGLISH} "Open clash:// links in Cute Clash. Selecting this replaces any existing app for these links. It is not selected automatically when another app is already registered."
LangString ProtocolDescription ${LANG_SIMPCHINESE} "使用 Cute Clash 打开 clash:// 链接。勾选后会替换现有默认应用；若已有其他应用注册，默认不勾选。"
LangString UnsupportedWindows ${LANG_ENGLISH} "Cute Clash requires Windows 7 Service Pack 1 or later. Install Windows 7 SP1 before continuing."
LangString UnsupportedWindows ${LANG_SIMPCHINESE} "Cute Clash 需要 Windows 7 Service Pack 1 或更高版本。请先安装 Windows 7 SP1。"
LangString WrongArchitecture ${LANG_ENGLISH} "This is the 64-bit installer. Please download the x86 installer for 32-bit Windows."
LangString WrongArchitecture ${LANG_SIMPCHINESE} "这是 64 位安装包。32 位 Windows 请下载 x86 安装包。"
LangString MissingLoader ${LANG_ENGLISH} "Windows is missing the secure DLL loading APIs required by the bundled .NET runtime. Install Microsoft KB3063858 (or a superseding Windows update), then restart Windows and run setup again. Setup has not changed your system."
LangString MissingLoader ${LANG_SIMPCHINESE} "系统缺少内置 .NET 运行时所需的安全 DLL 加载接口。请安装微软 KB3063858（或包含它的后续系统补丁），重启后再运行安装包。安装程序没有更改系统。"
LangString MissingUcrt ${LANG_ENGLISH} "Windows is missing the ${PRODUCT_ARCH} Universal C Runtime. Install Microsoft KB2999226 or a Windows 7-compatible Visual C++ 2015-2019 Redistributable, restart, then run setup again. You do not need .NET Framework. Setup has not changed your system."
LangString MissingUcrt ${LANG_SIMPCHINESE} "系统缺少 ${PRODUCT_ARCH} 通用 C 运行库。请安装微软 KB2999226 或兼容 Windows 7 的 Visual C++ 2015-2019 运行库，重启后再安装。无需安装 .NET Framework。安装程序没有更改系统。"
LangString AlreadyRunning ${LANG_ENGLISH} "Please exit Cute Clash / win7-clash from its tray menu before installing or uninstalling. Your proxy settings must be restored by the app before its files are changed."
LangString AlreadyRunning ${LANG_SIMPCHINESE} "请先从托盘菜单退出 Cute Clash / win7-clash，再安装或卸载。应用需要先恢复代理设置，安装程序才能更改文件。"
LangString SetupBusy ${LANG_ENGLISH} "Another Cute Clash installer or uninstaller is already running."
LangString SetupBusy ${LANG_SIMPCHINESE} "另一个 Cute Clash 安装或卸载程序正在运行。"
LangString UnsafeDirectory ${LANG_ENGLISH} "Choose a dedicated, empty local folder for Cute Clash. System folders, your profile or data folders, network paths, junctions, and folders containing unrelated files are not allowed.$\r$\n$\r$\n$INSTDIR"
LangString UnsafeDirectory ${LANG_SIMPCHINESE} "请为 Cute Clash 选择专用的空白本地文件夹。不能使用系统目录、用户或数据目录、网络路径、目录联接，或存有其他文件的文件夹。$\r$\n$\r$\n$INSTDIR"
LangString DifferentArchitecture ${LANG_ENGLISH} "This folder contains a different architecture of Cute Clash. Uninstall that version first; your profiles and settings will be kept."
LangString DifferentArchitecture ${LANG_SIMPCHINESE} "此目录已安装另一种位数的 Cute Clash。请先卸载旧版本；配置和设置会保留。"
LangString DifferentLocation ${LANG_ENGLISH} "Cute Clash is already installed in another folder. Select that folder to upgrade, or uninstall the old copy before choosing a new location. Profiles and settings are preserved by uninstall."
LangString DifferentLocation ${LANG_SIMPCHINESE} "Cute Clash 已安装到其他目录。请选择原目录升级，或先卸载旧版本后再选择新目录。卸载会保留用户配置和设置。"
LangString RemovalIncomplete ${LANG_ENGLISH} "Some program files could not be removed. Close applications using this folder, then run this uninstaller again. The uninstall entry and user data have been kept."
LangString RemovalIncomplete ${LANG_SIMPCHINESE} "部分程序文件无法删除。请关闭正在使用此目录的程序，然后再次运行卸载程序。卸载入口和用户数据已保留。"
LangString InvalidUninstall ${LANG_ENGLISH} "The installation marker and registered installation path do not match. No files were removed. Run the uninstaller from the original installation folder."
LangString InvalidUninstall ${LANG_SIMPCHINESE} "安装标记与注册的安装路径不一致，没有删除任何文件。请从原安装目录运行卸载程序。"
LangString FileLocked ${LANG_ENGLISH} "An installed program file is in use or is not writable. Close Cute Clash and its core, then try again. No files have been overwritten."
LangString FileLocked ${LANG_SIMPCHINESE} "程序文件正在使用或不可写。请关闭 Cute Clash 及其内核后重试，没有覆盖任何文件。"
LangString DataPreserved ${LANG_ENGLISH} "Your profiles, settings, and any files you added have been kept."
LangString DataPreserved ${LANG_SIMPCHINESE} "已保留用户配置、设置及自行添加的文件。"

; These functions are compiled independently into setup and uninstall.
!macro SharedFunctions PREFIX
Function ${PREFIX}AcquireSetupMutex
    System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Local\cute-clash-installer") p .r0 ?e'
    Pop $1
    StrCpy $SetupMutex $0
    ${If} $0 == 0
    ${OrIf} $1 == 183
        MessageBox MB_OK|MB_ICONSTOP "$(SetupBusy)" /SD IDOK
        SetErrorLevel 1618
        Quit
    ${EndIf}
FunctionEnd

Function ${PREFIX}CheckAppClosed
    StrCpy $AppRunning 0
    System::Call 'kernel32::CreateToolhelp32Snapshot(i 2, i 0) p .r0'
    StrCpy $ProcessSnapshot $0
    ${If} $0 == -1
        StrCpy $AppRunning 1
    ${Else}
        ; Unicode PROCESSENTRY32 for this 32-bit NSIS executable: 36-byte header + WCHAR[260].
        System::Alloc 556
        Pop $ProcessBuffer
        System::Call '*$ProcessBuffer(i 556)'
        System::Call 'kernel32::Process32FirstW(p $ProcessSnapshot, p $ProcessBuffer) i .r0'
        ${DoWhile} $0 != 0
            IntOp $1 $ProcessBuffer + 36
            System::Call '*$1(&w260 .r2)'
            ${If} $2 == "cute-clash.exe"
            ${OrIf} $2 == "win7-clash.exe"
                StrCpy $AppRunning 1
                ${ExitDo}
            ${EndIf}
            System::Call 'kernel32::Process32NextW(p $ProcessSnapshot, p $ProcessBuffer) i .r0'
        ${Loop}
        System::Free $ProcessBuffer
        System::Call 'kernel32::CloseHandle(p $ProcessSnapshot)'
    ${EndIf}
    ${If} $AppRunning == 1
        MessageBox MB_OK|MB_ICONSTOP "$(AlreadyRunning)" /SD IDOK
        SetErrorLevel 1618
        Quit
    ${EndIf}
FunctionEnd

Function ${PREFIX}CheckSafePath
    StrCpy $UnsafeReason 0
    ; NSIS GetFullPathName requires an existing path. The Win32 API also
    ; canonicalizes a new installation directory without creating it.
    System::Call 'kernel32::GetFullPathNameW(w "$INSTDIR", i ${NSIS_MAX_STRLEN}, w .r0, p 0) i .r1'
    ${If} $1 == 0
    ${OrIf} $1 >= ${NSIS_MAX_STRLEN}
        StrCpy $UnsafeReason 1
        Return
    ${EndIf}
    StrCpy $INSTDIR $0
    ${Do}
        StrLen $0 $INSTDIR
        ${If} $0 <= 3
            ${ExitDo}
        ${EndIf}
        StrCpy $1 $INSTDIR 1 -1
        ${If} $1 != "\"
            ${ExitDo}
        ${EndIf}
        StrCpy $INSTDIR $INSTDIR -1
    ${Loop}
    StrLen $0 $INSTDIR
    ${If} $0 < 4
    ${OrIf} $0 > 180
        StrCpy $UnsafeReason 1
        Return
    ${EndIf}
    ; Restrict to absolute local drive paths. Reject UNC/device namespaces and ADS.
    StrCpy $0 $INSTDIR 2 1
    ${If} $0 != ":\"
        StrCpy $UnsafeReason 1
        Return
    ${EndIf}
    StrCpy $0 $INSTDIR 3
    System::Call 'kernel32::GetDriveTypeW(w r0) i .r1'
    ${If} $1 != 2
    ${AndIf} $1 != 3
        StrCpy $UnsafeReason 1
        Return
    ${EndIf}
    StrCpy $0 3
    ${Do}
        StrCpy $1 $INSTDIR 1 $0
        ${If} $1 == ""
            ${ExitDo}
        ${EndIf}
        ${If} $1 == ":"
        ${OrIf} $1 == "*"
        ${OrIf} $1 == "?"
            StrCpy $UnsafeReason 1
            Return
        ${EndIf}
        IntOp $0 $0 + 1
    ${Loop}
    ${If} $INSTDIR == $PROFILE
    ${OrIf} $INSTDIR == $LOCALAPPDATA
    ${OrIf} $INSTDIR == $APPDATA
    ${OrIf} $INSTDIR == $DESKTOP
    ${OrIf} $INSTDIR == $DOCUMENTS
    ${OrIf} $INSTDIR == $TEMP
    ${OrIf} $INSTDIR == $SMPROGRAMS
    ${OrIf} $INSTDIR == $PROGRAMFILES
    ${OrIf} $INSTDIR == $PROGRAMFILES64
    ${OrIf} $INSTDIR == $COMMONFILES
        StrCpy $UnsafeReason 1
        Return
    ${EndIf}
    ; Walk ancestors: reject system and application-data trees, plus every reparse point.
    StrCpy $TargetPath $INSTDIR
    ${Do}
        ; Resolve existing 8.3 aliases before comparing protected directories.
        System::Call 'kernel32::GetLongPathNameW(w "$TargetPath", w .r0, i ${NSIS_MAX_STRLEN}) i .r1'
        ${If} $1 > 0
        ${AndIf} $1 < ${NSIS_MAX_STRLEN}
            StrCpy $TargetPath $0
        ${EndIf}
        ${If} $TargetPath == $WINDIR
        ${OrIf} $TargetPath == "$LOCALAPPDATA\cute-clash"
        ${OrIf} $TargetPath == "$LOCALAPPDATA\win7-clash"
            StrCpy $UnsafeReason 1
            Return
        ${EndIf}
        System::Call 'kernel32::GetFileAttributesW(w "$TargetPath") i .r0'
        ${If} $0 != -1
            IntOp $1 $0 & 0x400
            ${If} $1 != 0
                StrCpy $UnsafeReason 1
                Return
            ${EndIf}
        ${EndIf}
        ${GetParent} "$TargetPath" $TargetPath
        StrLen $0 $TargetPath
    ${LoopWhile} $0 > 2
FunctionEnd

Function ${PREFIX}CheckFilesUnlocked
    ; OPEN_EXISTING + GENERIC_WRITE + no sharing never truncates the file.
    ; Loaded executables cannot be opened for writing. This also catches a leftover core.
    ${If} ${FileExists} "$INSTDIR\cute-clash.exe"
        System::Call 'kernel32::CreateFileW(w "$INSTDIR\cute-clash.exe", i 0x40000000, i 0, p 0, i 3, i 0, p 0) p .r0'
        ${If} $0 == -1
            MessageBox MB_OK|MB_ICONSTOP "$(FileLocked)" /SD IDOK
            SetErrorLevel 1618
            Quit
        ${EndIf}
        System::Call 'kernel32::CloseHandle(p r0)'
    ${EndIf}
    ${If} ${FileExists} "$INSTDIR\core\mihomo.exe"
        System::Call 'kernel32::CreateFileW(w "$INSTDIR\core\mihomo.exe", i 0x40000000, i 0, p 0, i 3, i 0, p 0) p .r0'
        ${If} $0 == -1
            MessageBox MB_OK|MB_ICONSTOP "$(FileLocked)" /SD IDOK
            SetErrorLevel 1618
            Quit
        ${EndIf}
        System::Call 'kernel32::CloseHandle(p r0)'
    ${EndIf}
FunctionEnd

Function ${PREFIX}CheckPayloadPaths
    StrCpy $UnsafeReason 0
    ; Check every bundled path, including intermediate directories, before any write or deletion.
    !include "${BUILD_ROOT}\guard-paths.nsh"
FunctionEnd
!macroend
!insertmacro SharedFunctions ""
!insertmacro SharedFunctions "un."

Function .onInit
    SetShellVarContext current
    SetRegView 32
    !insertmacro MUI_LANGDLL_DISPLAY
    Call AcquireSetupMutex
    ${IfNot} ${AtLeastWin7}
        Goto UnsupportedOS
    ${EndIf}
    ${If} ${IsWin7}
    ${AndIfNot} ${AtLeastServicePack} 1
        Goto UnsupportedOS
    ${EndIf}
    !if "${PRODUCT_ARCH}" == "x64"
        ${IfNot} ${RunningX64}
            MessageBox MB_OK|MB_ICONSTOP "$(WrongArchitecture)" /SD IDOK
            SetErrorLevel 1633
            Quit
        ${EndIf}
    !endif
    System::Call 'kernel32::GetModuleHandleW(w "kernel32.dll") p .r0'
    System::Call 'kernel32::GetProcAddress(p r0, m "AddDllDirectory") p .r1'
    System::Call 'kernel32::GetProcAddress(p r0, m "SetDefaultDllDirectories") p .r2'
    ${If} $1 == 0
    ${OrIf} $2 == 0
        MessageBox MB_OK|MB_ICONSTOP "$(MissingLoader)" /SD IDOK
        SetErrorLevel 1633
        Quit
    ${EndIf}
    !ifndef BUNDLED_UCRT
    !if "${PRODUCT_ARCH}" == "x64"
        StrCpy $0 "$WINDIR\Sysnative"
    !else
        StrCpy $0 "$SYSDIR"
    !endif
    ${IfNot} ${FileExists} "$0\ucrtbase.dll"
        MessageBox MB_OK|MB_ICONSTOP "$(MissingUcrt)" /SD IDOK
        SetErrorLevel 1633
        Quit
    ${EndIf}
    !endif
    Call CheckAppClosed
    Call ConfigureProtocolSection
    Return
    UnsupportedOS:
        MessageBox MB_OK|MB_ICONSTOP "$(UnsupportedWindows)" /SD IDOK
        SetErrorLevel 1633
        Quit
FunctionEnd

Function ValidateInstallDirectory
    Call CheckSafePath
    ${If} $UnsafeReason != 0
        Goto BadDirectory
    ${EndIf}
    ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "InstallLocation"
    ${If} $0 != ""
    ${AndIf} $0 != $INSTDIR
        MessageBox MB_OK|MB_ICONSTOP "$(DifferentLocation)" /SD IDOK
        SetErrorLevel 1639
        Abort
    ${EndIf}
    Call CheckPayloadPaths
    ${If} $UnsafeReason != 0
        Goto BadDirectory
    ${EndIf}
    ${If} ${FileExists} "$INSTDIR\cute-clash-install.ini"
        ReadINIStr $0 "$INSTDIR\cute-clash-install.ini" "Install" "ProductId"
        ReadINIStr $1 "$INSTDIR\cute-clash-install.ini" "Install" "Path"
        ${If} $0 != "${PRODUCT_ID}"
        ${OrIf} $1 != $INSTDIR
            Goto BadDirectory
        ${EndIf}
        ReadINIStr $0 "$INSTDIR\cute-clash-install.ini" "Install" "Architecture"
        ${If} $0 != "${PRODUCT_ARCH}"
            MessageBox MB_OK|MB_ICONSTOP "$(DifferentArchitecture)" /SD IDOK
            SetErrorLevel 1633
            Abort
        ${EndIf}
    ${Else}
        FindFirst $0 $1 "$INSTDIR\*"
        ${DoWhile} $1 != ""
            ${If} $1 != "."
            ${AndIf} $1 != ".."
                FindClose $0
                Goto BadDirectory
            ${EndIf}
            FindNext $0 $1
        ${Loop}
        FindClose $0
    ${EndIf}
    Return
    BadDirectory:
        MessageBox MB_OK|MB_ICONSTOP "$(UnsafeDirectory)" /SD IDOK
        SetErrorLevel 1639
        Abort
FunctionEnd

Function DirectoryLeave
    Call ValidateInstallDirectory
FunctionEnd

Section "$(MainSection)" MainSection
    SectionIn RO
    ; Repeat validation here: silent /D= installs do not visit the directory page.
    Call ValidateInstallDirectory
    Call CheckAppClosed
    Call CheckFilesUnlocked
    SetOverwrite on
    SetOutPath "$INSTDIR"
    ; Write a product marker first, so a cancelled extraction can be retried safely.
    WriteINIStr "$INSTDIR\cute-clash-install.ini" "Install" "ProductId" "${PRODUCT_ID}"
    WriteINIStr "$INSTDIR\cute-clash-install.ini" "Install" "Path" "$INSTDIR"
    WriteINIStr "$INSTDIR\cute-clash-install.ini" "Install" "Architecture" "${PRODUCT_ARCH}"
    !include "${BUILD_ROOT}\install-files.nsh"
    SetOutPath "$INSTDIR"
    WriteINIStr "$INSTDIR\cute-clash-install.ini" "Install" "ProductId" "${PRODUCT_ID}"
    WriteINIStr "$INSTDIR\cute-clash-install.ini" "Install" "Path" "$INSTDIR"
    WriteINIStr "$INSTDIR\cute-clash-install.ini" "Install" "Architecture" "${PRODUCT_ARCH}"
    WriteINIStr "$INSTDIR\cute-clash-install.ini" "Install" "Version" "${PRODUCT_VERSION}"
    WriteUninstaller "$INSTDIR\uninstall.exe"
    CreateDirectory "$SMPROGRAMS\Cute Clash"
    CreateShortcut "$SMPROGRAMS\Cute Clash\Cute Clash.lnk" "$INSTDIR\cute-clash.exe"
    CreateShortcut "$SMPROGRAMS\Cute Clash\Uninstall Cute Clash.lnk" "$INSTDIR\uninstall.exe"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "Cute Clash (${PRODUCT_ARCH})"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "Cute Clash contributors"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\cute-clash.exe,0"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\uninstall.exe$\"'
    WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\uninstall.exe$\" /S'
    WriteRegStr HKCU "${UNINSTALL_KEY}" "URLInfoAbout" "https://github.com/Watertube-bilibili/cute-clash"
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" ${ESTIMATED_SIZE}
    WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallerLanguage" $LANGUAGE
SectionEnd

Section /o "$(DesktopSection)" DesktopSection
    CreateShortcut "$DESKTOP\Cute Clash.lnk" "$INSTDIR\cute-clash.exe"
    WriteINIStr "$INSTDIR\cute-clash-install.ini" "Install" "DesktopShortcut" "1"
SectionEnd

Section "$(ProtocolSection)" ProtocolSection
    WriteRegStr HKCU "Software\Classes\clash" "" "URL:Cute Clash subscription"
    WriteRegStr HKCU "Software\Classes\clash" "URL Protocol" ""
    WriteRegStr HKCU "Software\Classes\clash\DefaultIcon" "" '$\"$INSTDIR\cute-clash.exe$\",0'
    WriteRegStr HKCU "Software\Classes\clash\shell\open\command" "" '$\"$INSTDIR\cute-clash.exe$\" $\"%1$\"'
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
    !insertmacro MUI_DESCRIPTION_TEXT ${ProtocolSection} "$(ProtocolDescription)"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Function ConfigureProtocolSection
    ReadRegStr $0 HKCU "Software\Classes\clash\shell\open\command" ""
    ${If} $0 != ""
    ${AndIf} $0 != '$\"$INSTDIR\cute-clash.exe$\" $\"%1$\"'
        SectionSetFlags ${ProtocolSection} 0
    ${ElseIf} $0 == ""
        ; HKCR also sees an all-users handler. Respect it by default.
        ReadRegStr $0 HKCR "clash\shell\open\command" ""
        ${If} $0 != ""
            SectionSetFlags ${ProtocolSection} 0
        ${EndIf}
    ${EndIf}
FunctionEnd

Function un.onInit
    SetShellVarContext current
    SetRegView 32
    !insertmacro MUI_UNGETLANGUAGE
    Call un.AcquireSetupMutex
    Call un.CheckSafePath
    ${If} $UnsafeReason != 0
        Goto InvalidInstallation
    ${EndIf}
    Call un.CheckPayloadPaths
    ${If} $UnsafeReason != 0
        Goto InvalidInstallation
    ${EndIf}
    ReadINIStr $0 "$INSTDIR\cute-clash-install.ini" "Install" "ProductId"
    ReadINIStr $1 "$INSTDIR\cute-clash-install.ini" "Install" "Path"
    ReadRegStr $2 HKCU "${UNINSTALL_KEY}" "InstallLocation"
    ${If} $0 != "${PRODUCT_ID}"
    ${OrIf} $1 != $INSTDIR
    ${OrIf} $2 != $INSTDIR
        Goto InvalidInstallation
    ${EndIf}
    Call un.CheckAppClosed
    Call un.CheckFilesUnlocked
    Return
    InvalidInstallation:
        MessageBox MB_OK|MB_ICONSTOP "$(InvalidUninstall)" /SD IDOK
        SetErrorLevel 1639
        Quit
FunctionEnd

Section "Uninstall"
    ; Generated at build time from the exact bundled payload. Never use RMDir /r.
    SetOutPath "$TEMP"
    StrCpy $RemovalFailed 0
    !include "${BUILD_ROOT}\uninstall-files.nsh"
    ${If} $RemovalFailed != 0
        MessageBox MB_OK|MB_ICONSTOP "$(RemovalIncomplete)" /SD IDOK
        SetErrorLevel 5
        Abort
    ${EndIf}
    ReadINIStr $0 "$INSTDIR\cute-clash-install.ini" "Install" "DesktopShortcut"
    ${If} $0 == "1"
        Delete "$DESKTOP\Cute Clash.lnk"
    ${EndIf}
    Delete "$SMPROGRAMS\Cute Clash\Cute Clash.lnk"
    Delete "$SMPROGRAMS\Cute Clash\Uninstall Cute Clash.lnk"
    RMDir "$SMPROGRAMS\Cute Clash"
    ReadRegStr $0 HKCU "Software\Classes\clash\shell\open\command" ""
    ${If} $0 == '$\"$INSTDIR\cute-clash.exe$\" $\"%1$\"'
        DeleteRegKey HKCU "Software\Classes\clash"
    ${EndIf}
    DeleteRegKey HKCU "${UNINSTALL_KEY}"
    Delete "$INSTDIR\cute-clash-install.ini"
    Delete "$INSTDIR\uninstall.exe"
    RMDir "$INSTDIR"
    DetailPrint "$(DataPreserved)"
SectionEnd
