# Cute Clash

<!-- impeccable:product-schema 1 -->

## Platform

Native Windows desktop for Windows 7 SP1, Windows 10 and Windows 11. Retaining Windows 7 compatibility is a required minimum target.

## Stack

Implementation decision: C# 5 and Windows Forms, distributed with a private .NET 6.0.36 runtime and native support files. End users do not install .NET Framework. The original Framework 4.8 build remains available to developers. The interface has no WebView or Electron dependency. The proxy engine is managed separately from the interface.

## Product Purpose

Provide a Clash-compatible proxy client called **Cute Clash** that targets Windows 7, supports current protocols through its bundled engine, and offers TUN mode. The user explicitly permits bundling VxKex NEXT for components that require a compatibility layer.

## Users

Chinese- and English-speaking Windows users, including users retaining a Windows 7 computer who need current proxy protocol support.

## Operating Context

The user imports a local Clash YAML profile or a subscription URL, selects a profile and proxy, and connects. System proxy and TUN are separate choices. TUN requires administrator privileges. The client starts disconnected and can remain in the notification area.

## Capabilities and Constraints

- The interface must run natively on Windows 7 SP1.
- Use an updatable Clash-compatible engine; the engine determines protocol support.
- Provide configuration import and update, proxy selection and latency checks, routing mode, system proxy, TUN, logs, and port settings.
- Deliver an ordinary Windows installer and portable ZIP in a public GitHub Release whenever publishing source. Include the runtime and support `clash://` import links with confirmation.
- Configuration credentials are local private data; source URLs must not be shown in routine lists or copied into logs.
- Never claim a compatibility test passed without an actual test on the corresponding operating system.
- The user permits references to open-source proxy clients; preserve required attribution and licenses for reused code and distributed dependencies.

## Brand Commitments

The confirmed product name is **Cute Clash**, with the repository and executable slug `cute-clash`. Simplified Chinese is the initial language, and English is available in Settings. The user approved the original navy baby-kitten SVG in `assets/cute-clash.svg`, then explicitly requested a redesign of the entire interface. Preserve that approved icon. The new interface uses navy navigation, a light workspace and soft native controls without dropping Windows 7 compatibility.

## Product Principles

- Show the connection state and next useful action immediately.
- Make proxy changes explicit and restore owned system settings on exit.
- Prefer reliable native controls, keyboard operation, and clear error recovery.
- Keep engine compatibility facts distinct from verified operating-system behavior.

## Open Decisions

The exact range of Windows 7 editions, hardware, and installed system updates available for end-user testing is not yet established. No real subscription or node credentials were supplied.
