# Third-party notices

This project uses the following unmodified upstream artifacts. Preserve this directory when preparing a distribution.

| Component | Version | License / notice |
| --- | --- | --- |
| Mihomo | v1.19.31, commit `ab405bad5beeeac8b003bb01f60f134f6df54471` | GNU GPL version 3; see `mihomo-GPL-3.0.txt` and upstream source notices |
| Wintun official prebuilt DLLs | 0.14.1 | WireGuard LLC **Prebuilt Binaries License**, reproduced in `wintun-binary-LICENSE.txt` |
| YamlDotNet | 16.3.0, .NET Framework 4.7 assembly | MIT; see `YamlDotNet-MIT.txt` |

## Mihomo source and redistribution

Upstream project: <https://github.com/MetaCubeX/mihomo>

Exact release: <https://github.com/MetaCubeX/mihomo/releases/tag/v1.19.31>

The exact tag's source archive is saved locally as `dependencies/sources/mihomo-v1.19.31-source.zip` and pinned in `dependencies.lock.json`. It includes source code, `go.mod`, `go.sum`, build workflow, and project license files. Build workflow: <https://github.com/MetaCubeX/mihomo/blob/v1.19.31/.github/workflows/build.yml>. The Windows compatibility Go fork is <https://github.com/MetaCubeX/go>.

Mihomo is a separate, unmodified executable; the GUI controls it through its command line and local HTTP API. Calling it a separate program does not remove the distributor's obligations for the Mihomo binary itself. If distributing a binary bundle publicly, preserve notices and provide the corresponding source and build materials through a GPL-compliant method. Audit the exact Go module dependencies and applicable notices, including the Windows compatibility toolchain. The top-level source ZIP alone is **not claimed to be a complete, offline, reproducible corresponding-source bundle** of every linked dependency. This workspace preparation does not publish a binary release or create a public written source offer on behalf of the user.

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
