# cute clash 兼容性与依赖说明

核对日期：2026-09-22。目标系统为 **Windows 7 SP1，x86 或 x64，安装 .NET Framework 4.8**。这份说明区分上游兼容性声明与实际测试结果；当前开发机的运行测试不能替代 Windows 7 实机验收。

## 为什么采用原生桌面界面和 Mihomo

界面使用 Windows Forms / .NET Framework 4.8，避免依赖 WebView2、Electron、Tauri 或新版 Flutter 的系统要求。Microsoft 的[系统要求](https://learn.microsoft.com/en-us/dotnet/framework/get-started/system-requirements)列出 Windows 7 SP1 可安装 .NET Framework 4.8；4.8.1 不属于本项目的目标运行时。

Clash 是配置格式、控制接口和代理客户端生态的通称，并非单一的节点传输协议。本项目使用 **Mihomo v1.19.31** 解析 Clash / Mihomo YAML、处理规则与代理连接。该版本是核对日的[官方稳定版](https://github.com/MetaCubeX/mihomo/releases/tag/v1.19.31)。

[Mihomo 官方 FAQ](https://github.com/MetaCubeX/mihomo/wiki/FAQ)说明其官方 Windows 构建使用维护中的 Go 分支，支持 Windows 7 及以上。[v1.19.31 构建工作流](https://github.com/MetaCubeX/mihomo/blob/v1.19.31/.github/workflows/build.yml)也注明 Go 1.26 的 Windows 7 兼容补丁。因此本项目使用官方当前核心，**不需要为了 Windows 7 固定在已经停止演进的旧 Clash 核心**。这项声明不自动适用于用标准新版 Go 自行编译的核心。

| 部件 | 固定版本 / 文件 | 选择理由 |
| --- | --- | --- |
| x64 核心 | Mihomo v1.19.31，`windows-amd64-v1` | `v1` 是较低的 x64 指令集基线，适合较老 CPU；不是 `v3` 构建 |
| x86 核心 | Mihomo v1.19.31，`windows-386` | 为 32 位 Windows 7 提供独立核心 |
| TUN 驱动接口 | Wintun 0.14.1，匹配核心位数的官方 DLL | [官方说明](https://git.zx2c4.com/wintun/about/)包含 Windows 7；DLL 放在对应 `mihomo.exe` 旁 |
| YAML 库 | YamlDotNet 16.3.0，`lib/net47` | [NuGet 元数据](https://www.nuget.org/packages/YamlDotNet/16.3.0)列出 .NET Framework 4.7 目标、无包依赖，可供 .NET Framework 4.8 程序使用 |

核心支持的协议以这个固定版本的[Mihomo 配置文档](https://wiki.metacubex.one/en/config/proxies/)和配置校验结果为准，包括常见的 Shadowsocks / Shadowsocks 2022、VMess、VLESS、Trojan、Hysteria 2、TUIC、WireGuard、AnyTLS，以及相应的 Reality 等选项。不能由“支持新协议”推断为支持任何第三方私有扩展或未来协议；导入的配置仍须通过随附核心的校验。

## Windows 7 准备与 TUN

1. 使用 Windows 7 **SP1**。界面需要 [.NET Framework 4.8 离线安装程序](https://support.microsoft.com/en-us/servicing/dotnetframework/2019/10/microsoft-net-framework-4-8-offline-installer-for-windows)。
2. 安装匹配系统位数的服务堆栈更新 **KB4490628** 与 SHA-2 签名支持更新 **KB4474419**，然后重启。以 Microsoft [SHA-2 更新说明](https://support.microsoft.com/en-us/servicing/os/windows/2020/09/2019-sha-2-code-signing-support-requirement-for-windows-and-wsus)中更新版本及前置条件为准。旧系统缺少签名支持时，签名驱动的安装可能失败。
3. 保持系统根证书、时间及安全更新正常。下载脚本和订阅 HTTPS 使用正常的证书校验，不通过忽略证书错误来绕过旧系统问题。建议保持 Windows 7 后续平台及 DLL 加载相关更新完整。
4. TUN 创建虚拟网卡、配置路由，需要管理员权限；普通系统代理不需要创建虚拟网卡。应先保存工作，再在界面中选择管理员运行并启用 TUN。
5. `wintun.dll` 必须与实际启动的核心位数一致，放在核心旁边。无需手动复制 DLL 到 Windows 系统目录。驱动由 Wintun 接口按需加载。
6. 初次部署应检查防火墙、已有 VPN / 虚拟网卡及 DNS 配置冲突；如果 TUN 初始化失败，查看核心日志中的驱动或权限错误。

不要关闭驱动签名验证来掩盖缺少更新的问题。这里采用的 Wintun DLL 保持官方字节内容，未作二进制修改。

## VxKex NEXT 的定位

用户允许在需要时使用 VxKex NEXT。本版本的既定依赖已有上述原生 Windows 7 路线，因此没有捆绑、安装或自动启用兼容层，也不依赖它启动。

[VxKex NEXT](https://github.com/YuZhouRen86/VxKex-NEXT)提供 Windows API 扩展，让部分较新程序在旧系统上运行。它不是所有 Windows 10/11 程序或内核驱动的通用兼容保证。以后若某个可选部件确实需要它，应针对明确的程序版本、位数及功能做 Windows 7 测试，再决定是否作为可选安装项。不能用“已内置兼容层”替代 TUN 驱动与核心的实机验证。

## 下载、更新与完整性

`dependencies.lock.json` 锁定下载地址、版本、归档 SHA256、解包成员名和成员 SHA256。Mihomo 归档对照 GitHub release 资产 digest；Wintun 归档对照官网公布的 SHA2-256；YamlDotNet 对照 NuGet catalog 的 SHA512 后记录 SHA256。生成的源码 ZIP 及纯文本许可证没有独立上游签名，锁文件如实注明其哈希来自首次官方 HTTPS 下载。

开发机器运行以下命令可下载或校验，下载脚本要求 PowerShell 3.0+ 与 .NET Framework 4.8；**用户启动打包后的 GUI 不需要运行此脚本**。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\fetch-dependencies.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\fetch-dependencies.ps1 -VerifyOnly
```

下载或解包哈希不匹配时脚本停止，不运行下载文件、不降级到 HTTP、不忽略 TLS 错误。更新核心时应人工检查官方发布与兼容性说明，更新锁文件，再完成两种架构与 Windows 7 TUN 验证。当前版本固定不意味着今后无需更新；本项目没有把未经验证的 `latest` 核心自动覆盖到用户机器。

## 已完成与待完成的验证

- 已验证两份核心归档、Wintun 归档、NuGet 包与所有实际使用的成员哈希。
- 已在当前开发机执行 x64 和 x86 `mihomo.exe -v`，均报告 `Meta v1.19.31`、`go1.26.8`、`with_gvisor`。
- 已在当前开发机校验两份 Wintun DLL 的 Authenticode 签名，签名者为 WireGuard LLC，结果为 Valid。
- 已执行依赖脚本的准备流程与只读校验流程；在隔离测试目录验证了首次官方 HTTPS 下载，以及缓存文件被篡改后拒绝继续执行。
- **尚未在 Windows 7 SP1 实机 / 虚拟机完成 GUI、真实远程节点连通及 TUN 路由 / DNS 验证。** 不能将当前开发机的测试结果写成“Windows 7 已实测”。项目最终测试说明可补充后续结果。

Windows 7 验收至少应覆盖 x86/x64 启动、导入实际使用的订阅、代理连接、管理员 TUN 网卡创建、DNS / TCP / UDP 连通、停止后路由恢复、正常退出与异常退出后的系统代理恢复。

第三方许可证及源码对应关系见 `licenses/THIRD-PARTY-NOTICES.md`。
