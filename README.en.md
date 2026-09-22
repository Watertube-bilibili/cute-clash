<p align="center"><img src="assets/cute-clash.svg" alt="cute clash navy kitten" width="112" height="112"></p>

# cute clash

[简体中文](README.md) · **English**

A native Clash / Mihomo desktop client targeting **Windows 7 SP1**, with Chinese and English interfaces. Version **0.2.0** uses C# 5, Windows Forms and .NET Framework 4.8. Its original navy kitten SVG pays homage to Clash's feline theme. No Electron, WebView or browser runtime is required.

**Validation status:** the x86 and x64 builds have been tested on the Windows 11 development machine. Windows 7 startup, real remote proxy handshakes and TUN routing have **not** been tested on a Windows 7 machine or VM. Upstream compatibility statements are not a substitute for that acceptance test.

![cute clash English overview](docs/images/overview-en.png)

Navy navigation, a light workspace and the kitten icon appear across Overview, Profiles, Proxies, Settings and Logs. This preview was rendered by the application on the development machine with a sample profile.

## Get started

1. On Windows 7, install **SP1 and .NET Framework 4.8**, plus the **KB4490628** servicing stack update and **KB4474419** SHA-2 update, then restart. Use 4.8, not Windows 10/11-only 4.8.1. Sources and requirements are in [Compatibility](docs/COMPATIBILITY.md).
2. Extract the whole `cute-clash-0.2.0-win7-x64.zip` or `x86.zip` folder, matching your OS architecture, and run `cute-clash.exe`.
3. To switch from the default Chinese interface, open **设置 → 语言**, choose **English**, apply and restart the app when prompted. Switching language does not reconnect automatically.
4. Import a local YAML file or a **Clash / Mihomo YAML subscription URL** in Profiles. No proxy service or subscription is included. Raw Base64 node lists and individual `vless://` links are not converted.
5. Select a profile and connect. Enable System proxy for applications using Windows proxy settings, or configure HTTP / SOCKS5 `127.0.0.1:7890` directly in an app.
6. For **TUN**, use Settings to restart as administrator, enable TUN and connect. Wintun and the gVisor stack handle the virtual adapter, automatic routes and DNS interception. Initial driver loading can take time.
7. Choose groups and nodes in Proxies. Rule, global and direct modes are supported. Latency tests request `https://www.gstatic.com/generate_204` through the selected node.

Closing or minimizing the window hides it in the notification area. Use the tray's Exit command to stop completely. The app starts disconnected. Disconnecting and exiting attempt to restore previous WinINet proxy settings. A helper process and a Windows job object handle proxy recovery and core cleanup after an unexpected GUI exit. Recovery preserves changes made by another application or the user.

## Features

- YAML profile import, subscription updates, validation, selection and deletion.
- Proxy groups, node selection, latency tests and traffic counters.
- HTTP / SOCKS5 mixed listener, system proxy, rule / global / direct routing.
- Administrator TUN mode using Wintun, gVisor, automatic routes and DNS interception.
- Chinese / English UI, dialogs, tray menu and application diagnostics; original user names and core logs stay unchanged.
- Native tray, logs, configurable ports and administrator restart.
- Atomic configuration saves and rollback, random local API authentication and bounded YAML parsing.

The pinned engine is **Mihomo v1.19.31**, checked on 2026-09-22. It supports configurations for Shadowsocks / SS2022, VMess, VLESS / REALITY, Trojan, Hysteria2, TUIC v5, AnyTLS and WireGuard, among others. Exact interoperability depends on the core and server versions. [Protocol templates](examples/protocols-reference.yaml) pass the bundled core's syntax check but contain **no working servers or private credentials**. This is not a claim of support for every new server extension, XHTTP or future protocol.

[direct-test.yaml](examples/direct-test.yaml) forwards directly and is only a local functionality example. It does not provide a proxy service.

## Windows 7 compatibility

Official Mihomo Windows builds use a Go toolchain with Windows 7 compatibility patches. The x64 package uses the **amd64-v1** CPU baseline. The GUI targets .NET Framework 4.8, and the Wintun DLL matches the core architecture. VxKex NEXT is not required or bundled for this dependency set. See the [official Mihomo FAQ](https://github.com/MetaCubeX/mihomo/wiki/FAQ), [compatibility notes](docs/COMPATIBILITY.md) and [test matrix](docs/TESTING.md).

## Data and configuration

New installations use `%LOCALAPPDATA%\cute-clash`. If the former `win7-clash` data directory already contains settings and the new directory does not, the application reuses the existing directory in place. This preserves profiles without moving or overwriting files. Open the actual data folder from Settings.

Subscription URLs and credentials are local user data. Access is restricted to the current user, administrators and SYSTEM. Backups may also contain credentials. Log redaction hides URLs and common secret fields, but logs can still include domain names, IP addresses and node names.

Source profiles live in `profiles`; the managed `core/runtime.yaml` is removed when the core stops. The app owns inbound ports, API authentication, TUN, DNS listener addresses and loopback binding. It removes extra inbound listeners, external dashboards, script overrides and NTP clock-setting requests. Unknown node protocol fields are passed to Mihomo.

Provider caches are written under `providers/<profile-id>` with managed names. Absolute paths and directory traversal in imported provider paths are rejected. Local `type: file` provider contents must be placed in the managed path shown by the runtime configuration; arbitrary source-relative files are not copied automatically. HTTP/HTTPS providers download normally. GEO rules may require the core to fetch a database on first use.

System proxy changes affect the current user's default WinINet connection, including saved PAC / auto-detect settings for restoration. They do not change machine-wide WinHTTP or every VPN/dial-up connection. Use TUN when broader application coverage is required.

## Build

On Windows with **.NET Framework 4.8 and PowerShell 5.1 or later**:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\fetch-dependencies.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -RunTests -Package
```

The built-in .NET Framework C# compiler is sufficient; a current .NET SDK is not required. Portable outputs are in `dist`. Dependencies are pinned by HTTPS URL and checksum in `dependencies.lock.json`; downloads fail on a mismatch. Tests use isolated directories, a fake system-proxy store, local HTTP fixtures and the real core. They do not activate host TUN or change host proxy settings.

The SVG source is [assets/cute-clash.svg](assets/cute-clash.svg); PNG and a multi-resolution ICO are included. The executable and tray load the embedded ICO, so no SVG engine is needed at runtime. `scripts/render-icon.cjs` regenerates the derived icon files when the artwork changes.

When upgrading the engine, check official Windows 7 support, choose the matching x64-v1 or x86 build, verify its digest, update the lockfile and repeat target-system acceptance tests. The GUI does not hard-code a fixed protocol list or silently replace the core with an unverified latest build.

## License

The application and its original kitten artwork are GPL-3.0. Preserve [LICENSE](LICENSE) and the [third-party notices](licenses/THIRD-PARTY-NOTICES.md). Wintun's official DLL has a separate prebuilt-binary license. Downloaded cores, local application data, test outputs and credentials are excluded from the source repository.
