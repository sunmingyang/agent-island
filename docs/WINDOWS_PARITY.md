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

- **Top bar** — the signature Mac look, centered against the top edge
  (flat top corners). Best on screens where nothing is maximized under it.
- **Floating window** (default fallback for retired modes) — a draggable
  widget that remembers its spot and clamps to the work area. Because there
  is no camera housing to mimic, the 200 DIP notch-lookalike center gap
  tightens to a 64 DIP spacer in this mode (`UI/IslandModel.cs`,
  `NotchWidth`).
- The choice lives in `Model/IslandPositionStore.cs`
  (`AgentIsland.islandPlacement`) and is exposed in Settings → Display →
  Position. Bottom bar, tray-dock, edge/alignment, and vertical-rail
  variants were shipped experimentally and retired: Win11 cannot embed
  custom UI into the taskbar, and the extra modes read as clutter. Old
  preference values migrate to Floating.

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
