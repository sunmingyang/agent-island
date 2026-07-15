# Windows port — parity audit vs macOS

A working inventory of where `windows/` stands against the macOS app.
Kept as a living doc so contributors can pick gaps off the list.
Last full alignment pass: macOS `main` @ `8e22db6` (2026-07-16).

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

## Aligned to 8e22db6 (2026-07-16 pass)

| macOS change | Windows landing |
|---|---|
| Visual effects Calm/Vivid picker (calm-by-default, `6a69b7e`) | `Model/LowPowerModeStore.cs` keeps the historical `MacIsland.lowPowerMode` key: missing key → Calm, an explicit value survives the rename (old Low Power ON reads as Calm). `EffectiveEnabled` ORs in the Windows battery saver (`GetSystemPowerStatus.SystemStatusFlag`, re-read on `PowerModeChanged`) the way macOS folds in `isLowPowerModeEnabled`. Settings → Display → Top bar hosts the "Visual effects" picker (title + picker, no sentence). |
| Steady-glow gating semantics | Calm rests the halo and orbit sweep; hover / usage refresh / alerts light them; attention red and logo animations are never gated (`UI/IslandWindow.xaml.cs`, `UpdateHalo`/`UpdateSweep`). Platform note: macOS Vivid keeps the orbit always alive because Metal recomposites for free — the WPF layered window repaints the whole surface per frame (measured: one forever-animation ≈ 17–21% of a core), so Vivid here stays **activity-driven**, and Calm narrows further to glow events. `costStore.loading` has no Windows analog (cost scans are local file reads with no loading flag), so the glow-event set is hover / usage loading / alerts. |
| Copy sweep: trailing full stops stripped, wording upgrades (`0e99eca`) | `Localization/L10n.cs` gained an `EnglishTable` mirroring en.lproj (keys stay historical, display copy evolves) and the zh table was re-synced against zh-Hans.lproj; Windows-only keys got the same rule applied by hand. |
| "AgentIsland" → "Agent Island" everywhere user-visible | Window title, demo alarm meta, update dialog copy, launch-at-login subtitle. Internal identifiers (pref keys, mutex, paths, UA strings) intentionally keep the one-word form — renaming them would orphan user data. |
| Report cards v2 — share-clean, brand-teal, twin skeleton (`8e22db6`) | `UI/Report/ReportCards.cs`: QR + repo URL deleted; footer is a centered 30 pt app mark + "Agent Island"; WEEKLY/MONTHLY wordmark runs #20C0B0→#7DF0E3; weekly peak bar #7DF0E3→#20C0B0; monthly heat ramp white6% / #0B2F2A / #0E544B / #13877A / #20C0B0 with a teal glow on level 4; provider legends are 7 pt color dots (split bar keeps semantic provider colors); hero value line moves to liveTeal #3DD68C; per-card aura opacities match (weekly 0.13/0.11, monthly 0.14/0.09); monthly heat gap 20→18 so the two skeletons match. The "all tokens · incl. cache reads" caption macOS deleted never existed on Windows (zero-action). |
| authRequired = static red, not pulsing (`pulsesAttention`) | `Core/ActivityState.cs` adds `PulsesAttention` (stalled/rateLimited only); `UI/ProviderLogo.cs` gives authRequired a static red glow (no breath); `UpdateHalo` adds an `AttentionSteady` mode (red, 0.55, radius 42) between pulse and tints. |
| Black-panel heartbeat failsafe (`dbbd6e3` + `1453784`) | `UI/IslandWindow.xaml.cs` runs a 0.6 s `DispatcherTimer` **only while expanded**: state says expanded but content invisible (or opacity pinned ~0 well after the entrance ran) → re-run `ShowExpandedContent()`. The WPF choreography flips content in the same call as the state change, so the macOS timer races shouldn't exist here — the heartbeat is the same "never a legal steady state" invariant as insurance. |
| Classic wheel paging (`dbbd6e3`) | `UI/PagedContent.cs`: one notch = one page, 250 ms debounce (accelerated flicks don't skip across every screen), wheel-down advances; boundary-clamped via `ScreenPref.ShowNext`. Expanded-only by construction (the pager only exists in the expanded panel). |
| Auto solo layout / CLI footprint detection (`dbbd6e3`) | `Model/ProviderVisibilityStore.cs`: probes `~/.claude` \| `~/.config/claude` and `~/.codex` once at launch; `ClaudeShown`/`CodexShown` = manual-off wins, then a touched toggle always wins (`MacIsland.claudeVisibleTouched`/`codexVisibleTouched`), then detection hides a side only when the other side is present (neither footprint → both shown, never a blank island). All render paths read `Shown`; the Settings toggles bind to the stored choice. |

## Known remaining gaps (unordered)

- **Occlusion idling.** macOS pauses the sweep when the island window is
  covered (`WindowOcclusionStore`). Windows has no covered-window
  detection; instead all persistent animations are demand-driven (activity
  or glow events), which covers the idle case more broadly but not the
  "working but fully covered" case.
- **`ae5bafc` usage-layer items.** Transient-network retry with the
  `network drop — showing last data` subtitle, the `secondaryMissing`
  single-window Settings subtitle, and the jump-picker removal ("Open
  threads via" rows still exist on Windows). The `network drop` L10n key
  is already in place for when the retry lands.
- **Header wordmark tracking.** macOS letter-spaces AGENT ISLAND
  WEEKLY/MONTHLY at `tracking(3.2)`; WPF TextBlock has no letter-spacing
  property, so the wordmark runs at natural spacing.
- **Narrow-bar option dropped.** macOS offers Compact vs Notched bar
  widths; Windows hardcodes the wide layout (`UI/IslandModel.cs`).
- **Monitor identity is not replug-stable.** The display picker keys on
  `Screen.DeviceName` (`\\.\DISPLAY1`), which can shuffle across
  replug/reboot. macOS uses a stable display UUID; the Windows analog is
  the EDID/monitor path via `QueryDisplayConfig`.
- **No lock-screen hide.** macOS fades the island out on
  `com.apple.screenIsLocked`. Windows subscribes to `SessionSwitch` for
  usage freshness (unlock → `RefreshIfStale`) but does not hide the
  island while locked.
- **Alarm/dialog windows always center-screen.** They don't follow the
  island's monitor or edge (`Alarm/TurnAlarmWindow.cs`,
  `WindowStartupLocation.CenterScreen`).
- **Interface scale.** macOS gained an interface-scale setting
  (`1f97e4d`); Windows relies on system DPI scaling.
