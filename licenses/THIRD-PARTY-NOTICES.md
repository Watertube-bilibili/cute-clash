# Third-party notices

This project uses the following unmodified upstream artifacts. Preserve this directory when preparing a distribution.

| Component | Version | License / notice |
| --- | --- | --- |
| Mihomo | v1.19.31, commit `ab405bad5beeeac8b003bb01f60f134f6df54471` | GNU GPL version 3; see `mihomo-GPL-3.0.txt` and upstream source notices |
| Wintun official prebuilt DLLs | 0.14.1 | WireGuard LLC **Prebuilt Binaries License**, reproduced in `wintun-binary-LICENSE.txt` |
| YamlDotNet | 16.3.0, net6.0 in normal releases; net47 for the developer Framework build | MIT; see `YamlDotNet-MIT.txt` |
| .NET runtime / Windows Desktop | 6.0.36 | Original NuGet package licenses under `licenses/dotnet` in binary packages; Windows distribution terms in `dotnet-Library-LICENSE.txt`, plus original third-party notices |
| NSIS | 3.12 | Installer runtime and build-tool notices in `installer/NSIS-LICENSE.txt` in source and `licenses/NSIS-LICENSE.txt` in binary packages |

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
