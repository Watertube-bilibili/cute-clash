# cute clash

<!-- impeccable:product-schema 1 -->

## Platform

Native Windows desktop. Windows 7 SP1 is a required target; it is not a browser application.

## Stack

Implementation decision: C# 5, .NET Framework 4.8, and Windows Forms. This keeps the interface native on Windows 7 without a WebView or Electron dependency. The proxy engine is managed separately from the interface.

## Product Purpose

Provide a Clash-compatible proxy client called **cute clash** that targets Windows 7, supports current protocols through its bundled engine, and offers TUN mode. The user explicitly permits bundling VxKex NEXT for components that require a compatibility layer.

## Users

Chinese- and English-speaking users retaining a Windows 7 computer who need an actively maintainable proxy client.

## Operating Context

The user imports a local Clash YAML profile or a subscription URL, selects a profile and proxy, and connects. System proxy and TUN are separate choices. TUN requires administrator privileges. The client starts disconnected and can remain in the notification area.

## Capabilities and Constraints

- The interface must run natively on Windows 7 SP1.
- Use an updatable Clash-compatible engine; the engine determines protocol support.
- Provide configuration import and update, proxy selection and latency checks, routing mode, system proxy, TUN, logs, and port settings.
- Configuration credentials are local private data; source URLs must not be shown in routine lists or copied into logs.
- Never claim a compatibility test passed without an actual test on the corresponding operating system.
- The user permits references to open-source proxy clients; preserve required attribution and licenses for reused code and distributed dependencies.

## Brand Commitments

The confirmed product name is **cute clash**, with the repository and executable slug `cute-clash`. Simplified Chinese is the initial language, and English is available in Settings. The user approved the original navy baby-kitten SVG in `assets/cute-clash.svg`, then explicitly requested a redesign of the entire interface. Preserve that approved icon. The new interface uses navy navigation, a light workspace and soft native controls without dropping Windows 7 compatibility.

## Product Principles

- Show the connection state and next useful action immediately.
- Make proxy changes explicit and restore owned system settings on exit.
- Prefer reliable native controls, keyboard operation, and clear error recovery.
- Keep engine compatibility facts distinct from verified operating-system behavior.

## Open Decisions

The exact range of Windows 7 editions, hardware, and installed system updates available for end-user testing is not yet established. No real subscription or node credentials were supplied.
