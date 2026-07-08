# Windows port — parity audit vs macOS

A working inventory of where `windows/` stands against the macOS app,
focused on window positioning and the settings surface. Kept as a living
doc so contributors can pick gaps off the list.

## Positioning: how the two platforms differ

macOS has a hardware anchor the island is designed around: the notch. The
Mac app pins a fixed 900×360 transparent canvas top-center on the chosen
screen (`Sources/Window/IslandWindowController.swift`), detects the notch
via `safeAreaInsets` (`Sources/Model/NotchInfo.swift`), and the menu bar
reserves that strip of the screen so nothing else lives there.

Windows reserves nothing at the top of the screen. Maximized windows put
their tabs and title-bar buttons exactly where a top-center island sits,
and the taskbar can be docked to any edge. So the Windows port makes
placement a user choice instead of a fixed constant:

- **Edge** — `Top` (default, the signature look) or `Bottom` (sits on the
  work area, i.e. just above a bottom taskbar; the whole layout mirrors:
  bar strip against the edge, panel grows upward, corners round away from
  the edge).
- **Alignment** — `Left` / `Center` (default) / `Right` along that edge,
  with a 16 DIP inset on the sides. Left/right keep the island clear of
  browser tabs and caption buttons.
- Both live in `Model/IslandPositionStore.cs` (`AgentIsland.islandEdge`,
  `AgentIsland.islandAlignment`) and are exposed in Settings → Display →
  Position. Sides of the screen (vertical edges) were considered and
  rejected: the island is a wide horizontal bar that morphs into an
  800-DIP panel; a vertical-edge layout is a different product.

Placement resolves the target monitor from the Settings → Screen picker
(`Model/IslandTargetDisplayStore.cs`), converts the WinForms physical-pixel
work area into WPF DIPs via `TransformFromDevice`, and repositions on
`SystemParameters` changes, `SystemEvents.DisplaySettingsChanged`, and
store changes (`UI/IslandWindow.xaml.cs`, `PositionOnScreen` /
`ApplyEdgeLayout`).

## Fixed in this pass

| Gap | Where |
|---|---|
| "Show on" display picker persisted a choice nothing read — island always sat on the primary screen | `UI/IslandWindow.xaml.cs` now resolves `IslandTargetDisplayStore` and listens for changes |
| `Top = 0` ignored the work area — a top-docked taskbar overlapped the island | `PositionOnScreen` uses `Screen.WorkingArea` |
| No DPI conversion for non-primary monitors (WinForms pixels vs WPF DIPs) | `WorkAreaDip` transform |
| No edge/alignment choice at all | `IslandPositionStore` + Settings → Display → Position |
| `build.ps1` had no `-Version` parameter but `windows-release.yml` passes one — the next tag push would have failed the Windows release job | `build.ps1` accepts `-Version`, forwards `-p:Version` so the exe and Settings header carry the real version |
| No pre-tag compile check existed for the port (WPF cannot build on the Linux/macOS SDKs) | `.github/workflows/windows-ci.yml` builds + runs the test runner on `windows/**` pushes and PRs |

## Known remaining gaps (unordered)

- **Occlusion idling.** macOS pauses the sweep ring when the island is
  covered (`Sources/Model/WindowOcclusionStore.swift`), dropping idle CPU
  to ~0%. Windows only idles via Low Power Mode; there is no
  covered-window detection.
- **Auto-update is stubbed.** "Check now" shows a static dialog
  (`UI/SettingsWindow.cs`); the auto-check toggle persists a bool nothing
  reads. The release zip exists per tag, so a lightweight
  check-GitHub-releases updater is feasible.
- **Narrow-bar option dropped.** macOS offers Compact (100pt) vs Notched
  (200pt) bar widths; Windows hardcodes the wide layout
  (`UI/IslandModel.cs`). The `MacIsland.spacingMode` setter still persists
  a value the constructor ignores — dead code either way: wire it or
  remove it.
- **Monitor identity is not replug-stable.** The display picker keys on
  `Screen.DeviceName` (`\\.\DISPLAY1`), which can shuffle across
  replug/reboot. macOS uses a stable display UUID. A Windows analog is the
  EDID/monitor path via `QueryDisplayConfig`.
- **No lock-screen hide.** macOS fades the island out on
  `com.apple.screenIsLocked`. Windows could subscribe to
  `SystemEvents.SessionSwitch`.
- **Alarm/dialog windows always center-screen.** They don't follow the
  island's monitor or edge (`Alarm/TurnAlarmWindow.cs`,
  `WindowStartupLocation.CenterScreen`).
