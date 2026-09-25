# 开源实现来源与参考

这份记录区分实际移植的代码、随包分发的组件，以及阅读后独立实现的设计。具体许可证保留在 [第三方声明](../licenses/THIRD-PARTY-NOTICES.md) 和 `licenses` 目录中。

## 系统代理：移植 sysproxy-rs 的 Windows 处理方式

`src/SystemProxy.cs` 的 LAN 与 Unicode RAS 枚举、逐连接 WinINet 设置处理，依据 [sysproxy-rs Windows 源码](https://github.com/clash-verge-rev/sysproxy-rs/blob/44aaf00ec9c6779e5a461a55d882eddff7841c98/src/windows.rs) 移植到 C#。固定版本为 `44aaf00ec9c6779e5a461a55d882eddff7841c98`，上游采用 MIT 许可证，版权声明为 Copyright (c) 2022 zzzgydi。原始许可证位于 [sysproxy-rs-MIT.txt](../licenses/sysproxy-rs-MIT.txt)。

这样处理的目的，是同时覆盖默认 LAN 连接以及 Windows 拨号、宽带与 VPN 连接。Cute Clash 另行实现每条连接的独立备份、应用后的回读检查、部分失败恢复，以及避免覆盖其他软件后续修改的逻辑。

## TUN 防火墙：参考 Clash Party 的 Win7 修复

[Clash Party PR #1788](https://github.com/mihomo-party-org/clash-party/pull/1788) 将防火墙管理从 PowerShell NetSecurity 命令改为 `netsh advfirewall`，解决旧系统缺少相关 PowerShell 命令的问题。固定合并版本为 [`9ea9da7d586d2fd5115fc7cfeb42b2825210cb8e`](https://github.com/mihomo-party-org/clash-party/commit/9ea9da7d586d2fd5115fc7cfeb42b2825210cb8e)。

`src/WindowsFirewall.cs` 是根据这个兼容性方案独立编写的 C# 实现，没有复制其 TypeScript 源码。规则只对应 Cute Clash 当前核心的绝对路径，以路径哈希生成规则名，分开处理 TCP 和 UDP；不关闭系统防火墙，不删除其他客户端的规则。

## 订阅下载：参考显式选择网络路径与回退

阅读的固定版本包括：

- [Clash Party `src/main/config/profile.ts`](https://github.com/mihomo-party-org/clash-party/blob/364578f21007cdea0a5b4304acebfa7cf5655b1f/src/main/config/profile.ts)，commit `364578f21007cdea0a5b4304acebfa7cf5655b1f`。
- [Clash Verge Rev `src-tauri/src/feat/profile.rs`](https://github.com/clash-verge-rev/clash-verge-rev/blob/6a752994cf5f33c85a21ce3a590ff250b80e9846/src-tauri/src/feat/profile.rs)，commit `6a752994cf5f33c85a21ce3a590ff250b80e9846`。

Cute Clash 的 `src/SubscriptionDownloader.cs` 独立实现显式直连、可用时通过当前核心回退、连接和读取时限、下载与解压大小限制、HTTPS 重定向检查以及错误分类。下载时不自动发现系统代理，不绕过证书校验，也不把含密钥的订阅 URL 写入进度消息。

## TUN 与 DNS 默认值：参考 Clash Verge Rev

固定阅读版本为 `22e3f1ac8aefe4102ae2eb646a11a1ec614e8576` 的 [配置模板](https://github.com/clash-verge-rev/clash-verge-rev/blob/22e3f1ac8aefe4102ae2eb646a11a1ec614e8576/src-tauri/src/config/clash.rs) 与 [TUN 配置处理](https://github.com/clash-verge-rev/clash-verge-rev/blob/22e3f1ac8aefe4102ae2eb646a11a1ec614e8576/src-tauri/src/enhance/tun.rs)。这些是应用管理 TUN、DNS 字段保留及默认值的设计参考；未移植其 Rust 界面或执行框架。

## 实际代理与 TUN 核心

协议处理使用未经修改的 [Mihomo v1.19.31](https://github.com/MetaCubeX/mihomo/tree/ab405bad5beeeac8b003bb01f60f134f6df54471)。TUN 转发、Wintun 驱动调用、DNS 与路由管理由该核心及其上游依赖实现。完整源码材料与固定依赖说明见 [MIHOMO-SOURCE.md](MIHOMO-SOURCE.md)。Cute Clash 的本地握手、网卡和路由检查独立实现，不等于对远程节点、目标网站或 Win7 实机的端到端验收。

Clash Party 与 Clash Verge Rev 的应用源码采用 GPL；本项目独立编写的对应实现按 Cute Clash 的 GPL-3.0-or-later 分发。本项目未分发它们的应用二进制、图标或界面素材，也不暗示上游认可或提供支持。
