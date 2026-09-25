# Third-party notices

This project uses the following upstream artifacts and source adaptations. Preserve this directory when preparing a distribution.

| Component | Version | License / notice |
| --- | --- | --- |
| Mihomo | v1.19.31, commit `ab405bad5beeeac8b003bb01f60f134f6df54471` | GNU GPL version 3; see `mihomo-GPL-3.0.txt` and upstream source notices |
| Wintun official prebuilt DLLs | 0.14.1 | WireGuard LLC **Prebuilt Binaries License**, reproduced in `wintun-binary-LICENSE.txt` |
| YamlDotNet | 16.3.0, net6.0 in normal releases; net47 for the developer Framework build | MIT; see `YamlDotNet-MIT.txt` |
| .NET runtime / Windows Desktop | 6.0.36 | Original NuGet package licenses under `licenses/dotnet` in binary packages; Windows distribution terms in `dotnet-Library-LICENSE.txt`, plus original third-party notices |
| NSIS | 3.12 | Installer runtime and build-tool notices in `installer/NSIS-LICENSE.txt` in source and `licenses/NSIS-LICENSE.txt` in binary packages |
| sysproxy-rs Windows proxy implementation | commit `44aaf00ec9c6779e5a461a55d882eddff7841c98` | MIT; source adaptation in `src/SystemProxy.cs`, original notice in `sysproxy-rs-MIT.txt` |

## Windows system proxy source adaptation

The LAN and Unicode RAS connection enumeration and per-connection WinINet option application in `src/SystemProxy.cs` are adapted to C# from [sysproxy-rs `src/windows.rs`](https://github.com/clash-verge-rev/sysproxy-rs/blob/44aaf00ec9c6779e5a461a55d882eddff7841c98/src/windows.rs), exact commit `44aaf00ec9c6779e5a461a55d882eddff7841c98`. Upstream copyright: Copyright (c) 2022 zzzgydi. The original MIT license is preserved in `sysproxy-rs-MIT.txt`. Cute Clash adds separate recovery journals and readback checks for individual connections; no Rust binary or runtime from this project is bundled.

## Open-source design references

The following projects informed independently written C# implementations. These are implementation/design references, not a claim that their Rust or TypeScript source text or application binaries are included:

- **Clash Party firewall compatibility**: [PR #1788](https://github.com/mihomo-party-org/clash-party/pull/1788), merge commit [`9ea9da7d586d2fd5115fc7cfeb42b2825210cb8e`](https://github.com/mihomo-party-org/clash-party/commit/9ea9da7d586d2fd5115fc7cfeb42b2825210cb8e), replaces PowerShell NetSecurity commands with `netsh advfirewall` for Windows 7. Cute Clash uses its own command runner and rules scoped to the exact core executable, with separate TCP/UDP ownership and cleanup.
- **Clash Party subscription downloads**: [`src/main/config/profile.ts`](https://github.com/mihomo-party-org/clash-party/blob/364578f21007cdea0a5b4304acebfa7cf5655b1f/src/main/config/profile.ts), commit `364578f21007cdea0a5b4304acebfa7cf5655b1f`; reference for explicit direct/core-proxy download routes.
- **Clash Verge Rev subscription downloads**: [`src-tauri/src/feat/profile.rs`](https://github.com/clash-verge-rev/clash-verge-rev/blob/6a752994cf5f33c85a21ce3a590ff250b80e9846/src-tauri/src/feat/profile.rs), commit `6a752994cf5f33c85a21ce3a590ff250b80e9846`; reference for fallback between download routes.
- **Clash Verge Rev TUN configuration**: [`src-tauri/src/config/clash.rs`](https://github.com/clash-verge-rev/clash-verge-rev/blob/22e3f1ac8aefe4102ae2eb646a11a1ec614e8576/src-tauri/src/config/clash.rs) and [`src-tauri/src/enhance/tun.rs`](https://github.com/clash-verge-rev/clash-verge-rev/blob/22e3f1ac8aefe4102ae2eb646a11a1ec614e8576/src-tauri/src/enhance/tun.rs), commit `22e3f1ac8aefe4102ae2eb646a11a1ec614e8576`; references for application-owned TUN defaults and preserving provider DNS fields.

Clash Party and Clash Verge Rev publish their application source under the GPL. Cute Clash's own new implementations remain covered by this repository's GPL-3.0-or-later license. These references do not imply endorsement, and their interfaces, icons and application binaries are not redistributed. See [implementation references](../docs/OPEN-SOURCE-REFERENCES.md) for the division between source adaptation, bundled components and design references.

## Mihomo source and redistribution

Upstream project: <https://github.com/MetaCubeX/mihomo>

Exact release: <https://github.com/MetaCubeX/mihomo/releases/tag/v1.19.31>

The exact tag's source archive is saved locally as `dependencies/sources/mihomo-v1.19.31-source.zip` and pinned in `dependencies.lock.json`. It includes source code, `go.mod`, `go.sum`, build workflow, and project license files. Build workflow: <https://github.com/MetaCubeX/mihomo/blob/v1.19.31/.github/workflows/build.yml>. The Windows compatibility Go fork is <https://github.com/MetaCubeX/go>.

Mihomo is a separate, unmodified executable; the GUI controls its command line and local HTTP API. Release v0.3.0 supplies `mihomo-v1.19.31-complete-source.zip` beside the binary downloads, containing the exact tag archive, official vendor archive, the same release's patched Go toolchain with source, build workflow, actual build metadata and verification records. All module downloads passed `go mod verify`; the official vendor's 6,654 files match a freshly generated vendor byte-for-byte. See `docs/MIHOMO-SOURCE.md` for preparation and rebuild commands. The upstream binary is not represented as independently rebuilt byte-for-byte.

## Bundled .NET and native support libraries

The ordinary Windows packages publish the unmodified Microsoft runtime and Windows Desktop NuGet packages at exactly 6.0.36. Their original license files and package hashes are copied to `licenses/dotnet`. Windows distribution terms are preserved in `dotnet-Library-LICENSE.txt`; runtime/SDK third-party notices and the matching WinForms/WPF notices are included separately. Native UCRT/API-set/VC support files come from those official runtime packages, not from the developer machine's system directory. No separate Windows SDK UCRT files are used.

The application's GPL license does not relicense Microsoft's unmodified libraries or the separately licensed Wintun binary. The installer displays their separate terms alongside the application's license. .NET licensing references: https://github.com/dotnet/core/blob/main/license-information-windows.md and https://github.com/dotnet/core/blob/main/license-information.md .

## Wintun binary license

Download and checksum: <https://www.wintun.net/>

Source project (separately licensed): <https://git.zx2c4.com/wintun/>

The official signed `wintun.dll` files are governed by the **Prebuilt Binaries License included in the official ZIP**, not by the license of the source tree and not by MIT. The binaries must remain unmodified and proprietary notices retained. The license contains permission to distribute them alongside software that uses the permitted `wintun.h` API, as well as additional conditions. Consult the complete included license for the operative text. No endorsement by WireGuard LLC is implied.

## YamlDotNet

Package: <https://www.nuget.org/packages/YamlDotNet/16.3.0>

Source: <https://github.com/aaubry/YamlDotNet/tree/v16.3.0>

Copyright (c) Antoine Aubry and contributors. The original license text is included without changes. The selected `net47` assembly has no NuGet package dependencies.

## VxKex NEXT

VxKex NEXT was considered as an optional compatibility fallback. It is not included in this bundle and no files from that project are redistributed.
