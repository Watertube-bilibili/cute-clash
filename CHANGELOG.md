# Changelog

## 0.3.0

- Use **Cute Clash** consistently as the displayed product name across the app, tray, installer and release materials.
- Ship normal Windows x86 / x64 installers and portable ZIPs with .NET 6.0.36 and native runtime support files included; no system .NET Framework installation is required.
- Keep Windows 7 SP1 as the minimum target while supporting Windows 10 and Windows 11 with the same packages.
- Add `clash://install-config` subscription links, private same-user single-instance forwarding and an explicit import confirmation dialog.
- Add per-user setup, optional shortcuts and protocol association, and an uninstaller that preserves profiles and unrelated files.
- Preserve the approved kitten and existing interface, languages and old settings. Update runtime metadata only.
- Publish ready-to-use GitHub Release assets, checksums, application source and complete corresponding Mihomo source materials.

The .NET 6 series is out of Microsoft support. Windows 7 / Windows 10 machine acceptance and real remote TUN traffic remain pending; tests performed on Windows 11 do not establish those results.

## 0.2.0

- Rename the app to **Cute Clash**, with `cute-clash.exe` and matching package names.
- Add an original navy kitten SVG and embedded multi-resolution Windows icon.
- Redesign all five pages with navy navigation, a light workspace, a prominent connection panel and native rounded controls.
- Add persisted Simplified Chinese / English selection for pages, dialogs, tray actions and application diagnostics.
- Preserve existing win7-clash data in place when upgrading.
- Add bilingual readmes and source build verification on GitHub.
- Retain the Windows 7 SP1 / .NET Framework 4.8 target and pinned Mihomo / Wintun dependencies.

Windows 7 machine/VM acceptance and real TUN traffic validation remain pending.

## 0.1.0

Initial local version under the name win7-clash: profile import and subscriptions, proxy groups, system proxy, TUN integration and a native Windows interface.
