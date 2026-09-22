---
name: cute-clash
description: A calm navy connection console for a native Windows 7 proxy client.
colors:
  navy: "#172B4D"
  milk-workspace: "#F9F9F6"
  surface: "#FFFFFF"
  muted-ink: "#5B6B7E"
  divider: "#DEE5EE"
  navigation-muted: "#C4D2E6"
  navigation-selected: "#E9EFF8"
  navigation-hover: "#294063"
  navigation-pressed: "#314B70"
  action-hover: "#274168"
  action-pressed: "#0E1E37"
  secondary-hover: "#EBF0F6"
  secondary-pressed: "#E0E9F4"
  disabled-surface: "#E8ECF2"
  disabled-text: "#6C7A8C"
  log-text: "#E2EBF8"
typography:
  display:
    fontFamily: "Segoe UI (English), Microsoft YaHei (Simplified Chinese)"
    fontSize: 21pt
    fontWeight: 700
  state:
    fontFamily: "Segoe UI (English), Microsoft YaHei (Simplified Chinese)"
    fontSize: 20pt
    fontWeight: 700
  title:
    fontFamily: "Segoe UI (English), Microsoft YaHei (Simplified Chinese)"
    fontSize: 12pt
    fontWeight: 700
  body:
    fontFamily: "Segoe UI (English), Microsoft YaHei (Simplified Chinese)"
    fontSize: 9pt
    fontWeight: 400
  metric:
    fontFamily: "Segoe UI (English), Microsoft YaHei (Simplified Chinese)"
    fontSize: 22pt
    fontWeight: 400
  mono:
    fontFamily: Consolas
    fontSize: 9pt
    fontWeight: 400
rounded:
  button: 8px
  navigation: 9px
  routing: 12px
  surface: 14px
  connection: 16px
spacing:
  action-gap: 8px
  section-gap: 14px
  sidebar-inset: 16px
  content-inset: 28px
components:
  primary-action:
    backgroundColor: "{colors.navy}"
    textColor: "{colors.surface}"
    rounded: "{rounded.button}"
    height: 36px
  connection-action:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.navy}"
    rounded: "{rounded.button}"
    height: 45px
  secondary-action:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.navy}"
    rounded: "{rounded.button}"
    height: 36px
  navigation:
    backgroundColor: "{colors.navy}"
    textColor: "{colors.navigation-muted}"
    rounded: "{rounded.navigation}"
    width: 164px
    height: 45px
  navigation-selected:
    backgroundColor: "{colors.navigation-selected}"
    textColor: "{colors.navy}"
---

# Design System: Cute Clash

## Overview

**Creative North Star: The Navy Connection Console.**

The approved navy kitten anchors a compact, native Windows utility. A dark navigation rail and connection panel establish a stable place for the most consequential state and action. Warm milk surfaces make the rest of the application calm and readable. Soft corners belong to the application frame, panels and buttons; operating-system fields, lists, scrollbars, dialogs and window chrome retain native behavior.

The core workflow is to import a profile, choose how traffic is captured, connect, and inspect real proxy groups or logs. The interface never manufactures traffic, latency or a connected state for visual completeness. The custom painting uses Windows Forms and GDI+, with no browser, external font service or SVG rendering engine at runtime.

Preserve the approved kitten in `assets/cute-clash.svg`, Windows 7 compatibility constraints and existing connection behavior. The direction keeps critical state in a stable position and avoids glowing gauges or ornamental data.

## Colors

Navy carries navigation, running controls, primary actions and the log console. Milk is the workspace, and white surfaces group editable controls and lists. Pale blue selected navigation provides a clear position within the five work pages. Navy-tinted secondary text keeps the navigation readable; slate text is reserved for the light workspace.

Status is always described in words. Connected, disconnected, busy, unavailable and selected states do not rely on color alone. Disabled actions use a distinct pale fill and muted text. The dark log viewer uses pale text with native scrollbars.

## Typography

English uses **Segoe UI** and Simplified Chinese uses **Microsoft YaHei**, both consistent with the Windows 7 target. All type sizes are 96-DPI design values and scale with the window. Native text fields inherit the active language font. **Consolas** is used only for the runtime log data.

Page titles are 21pt bold; the connection state is 20pt bold. Section headings and the active profile use 12pt bold. The kitten brand uses 14.5pt bold. Body copy and controls use 9pt, and runtime totals use 22pt without decorating absent values. Lists measure their actual live font, including bold selected values, so timestamp and latency columns remain legible after DPI scaling.

## Layout

The initial client area is 1024 × 680 at the 96-DPI baseline. The minimum outer window remains 920 × 680. A 196px navy rail has 16px side insets. Its approved kitten sits on a light 47px badge next to the product name; a platform note and persistent connection state remain near the bottom. Navigation buttons are 164 × 45px with authored GDI+ line symbols and a 7px gap.

Content has 28px horizontal padding, 24px top padding and a shared 82px header. The header's connection action appears on Profiles, Proxies, Settings and Logs. Overview moves that action into the dominant connection panel, avoiding duplicate actions in the same viewport.

- **Overview:** a 136px navy connection region with explicit state, contextual hint and a light connection button; the active profile and management action; independent system proxy/TUN checkboxes beside routing mode; real transfer totals and active connections; local endpoint and administrator guidance.
- **Profiles:** import actions above a flexible white list surface, with selected-profile actions and contextual guidance below. An empty profile collection replaces the blank table with a clear import prompt. Source URLs remain private.
- **Proxies:** one white master/detail surface, with groups occupying 29% and nodes 71%; a softly tinted group selector clarifies the master column. Refresh and node operations retain real enabled/disabled states.
- **Settings:** three vertically stacked sections—interface language, local connection, core/app—within one scrollable page. English is therefore findable before the ports or about information. Full-width notes wrap naturally and the bottom section remains available through native vertical scrolling.
- **Logs:** clear/copy controls above a large navy console with native scrolling and an explicit privacy note.

DPI scaling starts only after all controls exist. Native list-view column widths are measured from their current font instead of relying on the form's scaling mechanism. Controls in auto-sized tables carry minimum dimensions where needed; the Apply ports button, numeric fields and data-folder action must not collapse. The minimum viewport preserves connection controls and allows Settings to scroll vertically.

## Elevation & Depth

No custom shadows or blur. Tonal contrast supplies hierarchy. Light list and settings surfaces use one thin divider-colored border; the navy connection and log panels use their fill alone. Native window chrome, menus and dialogs remain operating-system surfaces.

## Shapes

`SoftPanel` paints DPI-aware rounded boundaries, generally 14px, with 16px reserved for the connection panel. Routing uses 12px and actions use 8px. The approved kitten stays an actual provided vector-derived asset, never a geometric approximation. Its embedded multi-size ICO supplies the titlebar/tray icon and a 128px source for the sidebar badge.

Checkboxes, numeric inputs, text fields, combo boxes and list-view mechanics remain native rectangles. This split makes the application feel consistent while preserving Windows 7 input affordances.

## Components

**Actions.** `SoftButton` derives from Windows Forms `Button`; custom paint changes its appearance while preserving click events, keyboard activation, dialog results and accessibility roles. It draws explicit hover, pressed, disabled and keyboard-focus states. Primary actions use navy, secondary actions white with a thin edge, and the main connection button reverses white over navy. Busy controller operations disable affected actions.

**Navigation.** Labels and authored line symbols share a fixed baseline. The selected destination has a pale fill, navy text and bold weight. The five symbols represent overview, profiles, proxy groups, settings and logs; they are drawn directly rather than using Unicode/emoji substitutions.

**Inputs.** System proxy and TUN remain independently selectable checkboxes. Routing is an explicit rule/global/direct dropdown. Language changes require the Apply language action: preferences are saved immediately, then the user can restart now or later. A restart disconnects the core and does not auto-connect. UI synchronization and dropdown selection alone do not save settings.

**Data and empty states.** Profile and node tables preserve native full-row selection and Enter/double-click activation. Real names and core log output are never translated. Missing metrics remain an em dash. Unavailable proxy groups and profile collections explain the next action rather than showing demonstration nodes or fabricated statistics.

**Notification area.** The application/tray icon uses the approved kitten. Closing or minimizing the window keeps the application in the notification area. Double-click restores the window, and the localized tray menu offers connection control and explicit disconnection/exit. Settings describes this behavior.

## Do's and Don'ts

- Preserve Windows Forms input semantics, keyboard focus, native selection and the existing controller behavior.
- Keep critical connection state, selected profile and capture mode visible before secondary information.
- Add all application-owned copy in Chinese and English; retain user-supplied profile/node names exactly.
- Use screenshots of both languages, the minimum window, real empty data, and actual core-connected states when changing layout.
- Do not add fake charts, example latency, decorative gauges, automatic reconnection or a WebView requirement.
- Do not use rounded containers for every label; the overview's profile and runtime information stay directly on the workspace.
- Do not describe host screenshots or unit tests as Windows 7 guest or live TUN acceptance. Those still require the corresponding environment.

Verification evidence is stored in `artifacts/ui-v03`: both languages, all five work pages, minimum-size pages, profile empty state, a real Mihomo core-connected overview/proxy view, and the scrolled Settings bottom. No host system-proxy change or TUN activation was used to create those captures.
