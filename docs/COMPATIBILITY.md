# Windows 兼容性与运行环境

核对日期：2026-09-22。Cute Clash 是 Windows 桌面软件，面向 **Windows 7 SP1、Windows 10、Windows 11**，并持续保留 Windows 7 兼容性。32 位系统用 x86，64 位系统建议 x64。

## 不需要安装系统 .NET

0.3.0 的安装包和便携 ZIP 都使用 Windows Forms，自带 **.NET 6.0.36**、对应架构的 CoreCLR、Windows Desktop 组件、UCRT API-set DLL 和 `vcruntime140_cor3.dll`。这些文件只在应用目录内使用，不注册或安装系统级 .NET，也不修改 Windows 的 .NET Framework。用户不需要安装 SDK、PowerShell、.NET Framework 或另一个 .NET Runtime。

系统 .NET Framework 安装失败不会再阻止这个版本仅因缺少 Framework 而启动。若 Windows 自身缺少加载器补丁、系统文件损坏或驱动条件不满足，仍需要处理具体系统问题；应用本地运行时不能代替 Windows 系统组件。

微软的 [Windows .NET 安装说明](https://learn.microsoft.com/en-us/dotnet/core/install/windows#windows-7--81--server-2012) 将 .NET 6 列为最后一个兼容 Win7 的系列。最终版本是 6.0.36，已经于 2024-11-12 停止官方维护；此项目保留这个生命周期限制。较新的 Mihomo 核心单独维护，不随 GUI 运行时一起冻结协议能力。

应用采用自包含发布，不裁剪、不合并为单个 EXE、不启用 ReadyToRun，便于核验实际加载的 DLL。运行时使用官方 NuGet 发布包原始字节；许可与第三方 notices 随包保留。

## 各系统要求

| 系统 | 准备事项 | 当前验证状态 |
| --- | --- | --- |
| Windows 7 SP1 x86 / x64 | SP1；KB3063858 或提供相同加载接口的替代更新；使用签名驱动还需完整 SHA-2 / 服务堆栈更新 | 保留的最低系统目标，尚无实机 / VM 验收 |
| Windows 10 x86 / x64 | 安装或完整解压对应发行包 | 目标系统，尚无独立实机验收 |
| Windows 11 x64 | 安装或完整解压 x64 发行包；x86 包也可在兼容环境运行 | 开发机执行 x64/x86 程序、实际内核及安装流程检查 |

Windows 7 的相关官方依据：

- [Microsoft .NET Windows 前置条件](https://learn.microsoft.com/en-us/dotnet/core/install/windows#windows-7--81--server-2012)：KB3063858 提供所需的安全 DLL 加载支持。安装器检查 `AddDllDirectory` / `SetDefaultDllDirectories` 是否存在，接受提供接口的替代更新。
- [SHA-2 签名支持说明](https://support.microsoft.com/en-us/servicing/os/windows/2020/09/2019-sha-2-code-signing-support-requirement-for-windows-and-wsus)：包括 KB4490628 服务堆栈与 KB4474419 SHA-2 更新，安装后按要求重启。
- [UCRT 部署说明](https://learn.microsoft.com/en-us/cpp/windows/universal-crt-deployment)：允许应用本地部署 UCRT。当前包使用 .NET 官方包自带文件，不复制开发机 System32 文件，不覆盖系统 DLL。

系统时间、根证书及 HTTPS 信任链仍需正常。不会关闭证书或驱动签名校验。

## 协议核心与 TUN

| 部件 | 版本 | 选择说明 |
| --- | --- | --- |
| Mihomo | v1.19.31 | 官方 Windows amd64-v1 / 386；x64 v1 适合较老 CPU |
| Wintun | 0.14.1 | 与核心位数匹配的官方签名 DLL；放在 `core` 内 |
| YamlDotNet | 16.3.0 | 普通包使用 net6.0 程序集；开发者 Framework 工程使用 net47 |
| GUI runtime | .NET 6.0.36 | 每个包内自带对应位数运行环境 |

[Mihomo 官方 FAQ](https://github.com/MetaCubeX/mihomo/wiki/FAQ) 与 [v1.19.31 构建工作流](https://github.com/MetaCubeX/mihomo/blob/v1.19.31/.github/workflows/build.yml) 说明官方 Windows 构建使用带 Win7 兼容补丁的 Go 工具链。本次实际二进制报告 Go 1.26.8、`with_gvisor`。官方 tag 源码、vendor 和补丁工具链源码均随 Release 提供，见 [源码材料说明](MIHOMO-SOURCE.md)。

Clash 是配置格式、控制接口和客户端生态的通称，不是单一节点传输协议。当前核心可处理 Shadowsocks / SS2022、VMess、VLESS / Reality、Trojan、Hysteria2、TUIC v5、WireGuard、AnyTLS 等配置。模板只做语法校验，不代表所有服务器和新扩展都完成互通测试。

TUN 使用 Wintun + gVisor，需要管理员权限创建网卡和路由。程序安装与普通系统代理操作不需要因此提升安装器权限。真实 TUN DNS/TCP/UDP 流量、停止后路由恢复及其他 VPN 共存仍需目标机验收。

## VxKex NEXT

当前依赖选择有原生 Win7 路线，没有安装、打包或自动启用 VxKex NEXT。它保留为未来特定组件的候选兼容措施，不用于替代核心、驱动和目标系统验证。

## 开发与实测边界

`runtime.lock.json` 固定 SDK 6.0.428 的官方 SHA512 和运行时 6.0.36；`dependencies.lock.json` 固定核心、Wintun、YamlDotNet 的归档及成员哈希；`installer/toolchain.lock.json` 固定 NSIS。下载先校验，不关闭 HTTPS 校验。

实际覆盖范围见 [TESTING.md](TESTING.md)。仍需 Windows 7 SP1 实机或虚拟机的界面、DPI、驱动、远程节点与 TUN 验收。支持范围是构建目标，不能等同于所有系统都已经测试。
