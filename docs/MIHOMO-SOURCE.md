# Mihomo v1.19.31 source materials

`mihomo-v1.19.31-complete-source.zip` accompanies the Cute Clash binary Release. It contains the actual Mihomo source and dependency materials, not only links to upstream projects.

## Included materials

| File | Contents |
| --- | --- |
| `mihomo-v1.19.31-source.zip` | Unmodified official tag source at commit `ab405bad5beeeac8b003bb01f60f134f6df54471`, including its license, build files and workflows. |
| `vendor.tar.gz` | The vendor archive published in the **same Mihomo v1.19.31 Release**: dependency package sources, their license files and `vendor/modules.txt`. |
| `toolchain.tar.gz` | The toolchain archive published in that same Release. It contains the **MetaCubeX-patched Go 1.26.8 source tree and license**, together with the upstream-provided Linux amd64 compiler tools. This is not a Windows runtime dependency. |
| `upstream-build.yml`, `go.mod`, `go.sum`, `LICENSE` | Convenient copies of the unchanged tag's build workflow, dependency manifest/checksums and GPL license. |
| `core-build-info-x64.txt`, `core-build-info-x86.txt` | `go version -m` output read from the exact official core executables distributed with Cute Clash. |
| `core-version-*.txt` | Core version, build date and build tags reported by those executables with `-v`. |
| `SOURCE-MANIFEST.json`, `SHA256SUMS.txt` | Pinned upstream URLs, SHA256 digests, source commit, preparation results and package-file checksums. |
| `vendor-verification.txt`, `vendor-SHA256SUMS.txt`, `go-mod-verify.txt`, `modules.txt` | Evidence that vendored files match a regeneration from checksum-verified modules and that upstream `go.mod` / `go.sum` were not changed. |

The source archive and its vendor/toolchain tarballs deliberately preserve the upstream files. Dependencies retain their own licenses. The toolchain tarball includes its source under `go/src`; the archive also contains Go library/support files and Linux compiler executables. There is no need to install or run that toolchain to use Cute Clash.

## Prepare and check the materials again

On the Cute Clash repository checkout, first fetch its pinned dependencies, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/fetch-dependencies.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/prepare-mihomo-source.ps1
```

Preparation runs on the development host. It downloads the three pinned source materials and a SHA256-pinned Windows build of MetaCubeX Go 1.26.8 into `.tools`. All Go caches are isolated under `artifacts/mihomo-source`; existing global Go settings are restored afterward. Modules are downloaded through `https://proxy.golang.org`, checked against upstream `go.sum` with checksum-database verification enabled, and checked again with `go mod verify`. A newly generated vendor tree is compared with the official vendor archive, file by file, including sources, licenses and the module manifest. Preparation fails if the inputs or vendor contents differ. The module cache is kept locally for repeat checks; it is not duplicated in the final ZIP because the verified vendor sources are already included.

The copy of `prepare-mihomo-source.ps1` inside this ZIP is a reference copy of the repository script. Run it from its normal `scripts/` location in a Cute Clash checkout, as it expects that checkout's dependency lock and official core files.

## Build from the included sources

The bundled upstream workflow is authoritative. It uses the patched MetaCubeX toolchain for Windows 7 compatibility. A stock recent Go release is not an equivalent replacement for that patch set.

On a Linux amd64 build host, unpack the toolchain tarball, unpack the Mihomo source ZIP, and unpack `vendor.tar.gz` inside the `mihomo-1.19.31` source directory so that `vendor/modules.txt` is directly below it. No Go module download is needed for a vendored build. For example, from that source directory:

```sh
export GOROOT=/absolute/path/to/extracted/go
export PATH="$GOROOT/bin:$PATH"
export GOTOOLCHAIN=local
export CGO_ENABLED=0
export GOOS=windows
export GOARCH=amd64
export GOAMD64=v1
export GOPROXY=off
export GOSUMDB=off

go build -mod=vendor -tags with_gvisor -trimpath \
  -ldflags "-extldflags --static -X 'github.com/metacubex/mihomo/constant.Version=v1.19.31' -X 'github.com/metacubex/mihomo/constant.BuildTime=your-build-date' -w -s -buildid=" \
  -o mihomo-windows-amd64-v1.exe .
```

For the 32-bit build, set `GOARCH=386`, unset `GOAMD64`, and use `mihomo-windows-386.exe` as the output name. The upstream matrix leaves `GO386` unspecified for this target; use the bundled toolchain's default. `with_gvisor`, `CGO_ENABLED=0`, `-trimpath`, the version/build-time variables and the stripped build ID are the upstream build settings. The exact compiler version, architecture flags, dependency versions and VCS metadata recorded in the distributed binaries can be inspected in `core-build-info-*.txt`.

## What has and has not been verified

Preparation verifies archive SHA256 values, the dependency module checksums, unchanged source manifests and byte-for-byte agreement between the official and regenerated vendor trees. The source commit is pinned to the Mihomo tag, and the toolchain source is pinned by the same release's archive digest rather than an inferred Go-fork Git commit.

The upstream build also updates `component/ca/ca-certificates.crt` from the Ubuntu runner's current CA store immediately before compilation, and records a build timestamp. The original tag retains its checked-in CA bundle. The precise runtime CA update and all original build-host details have not been independently reconstructed, and **the core has not been rebuilt or proven byte-for-byte reproducible** during this preparation. The package supplies the complete upstream tag/vendor/patched-toolchain source materials and recorded build instructions; it does not claim a binary reproduction that was not performed.
