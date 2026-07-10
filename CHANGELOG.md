# Changelog

User-facing changes per release. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); dates are when the
tag was cut.

## [1.5.2] - 2026-07-10

### Fixed
- "Open thread" on a Claude Desktop session brings Claude Desktop forward again instead of popping a Terminal (both platforms). CLI sessions still resume for real via `claude --resume` from the session's own directory — there is no external deep link that lands on an existing Desktop conversation, so app-level focus is the honest ceiling there.
- App icon rebuilt on Apple's icon grid with transparent margins: no more white corners on macOS 15, no more system backing plate on macOS 26. (Windows .ico was already clean.)
- Release automation: notes are now reliably taken from this CHANGELOG — the publish step had silently no-op'd since 1.2.3 — and a missing section now fails the release instead of shipping a stub.

### Added
- Demo mode can render both alarm cards headlessly to PNG (release screenshots without screen-recording permission).

## [1.5.1] - 2026-07-10

First version-aligned macOS + Windows release: one tag, both platforms, same detection-engine behavior.

### Added
- Out-of-quota alarm: a distinct full-screen alarm the moment a 5-hour or weekly window hits 100%, with the reset time on it. Fires once per reset cycle, warms up on launch, respects the master alarm switch.
- In-app browser re-auth for Claude: the Re-authenticate button opens the claude.com authorize page in your default browser and finishes via a local loopback callback — no Terminal, no code pasting. The CLI flow remains as fallback.
- Settings toggle "Alarm on subagent threads" (off by default) on both platforms.

### Fixed
- Rate-limit / API-error lines in Claude transcripts (`isApiErrorMessage`) no longer fire a false "It's your turn" alarm.
- Codex subagent (child) threads no longer raise turn alarms or drive the logo by default.
- "Open thread" reliability: the `codex://` deep link is delivered to the running Codex app instead of a stale duplicate handler; Claude sessions resume via `claude --resume` in a terminal (the `claude://resume` deep link only ever started a new session and is gone).
- Usage sync can no longer freeze on a wedged fetch — 25s request timeouts plus a loading watchdog.
- Auto-resume safety: sessions with no recorded project path fail closed instead of being auto-trusted; blocked interval triggers back off a full cycle instead of retrying every minute.

### Changed
- Cost page hidden by default on both platforms (Settings → Display to re-enable).
- Windows: brand tray icon with usage ring and state color; simplified placement (top bar or floating widget).

## [1.4.1] - 2026-07-05

### Fixed
- Codex automation rollouts (probes, orchestrator subagents, `codex exec`) no longer raise turn alarms or drive the logo.
- Forced language now applies everywhere immediately (date formatters no longer freeze the old locale); the misleading "restart required" alert is gone.
- Display tab: the two "Top bar" sections are merged into one.
- Alarm sounds preview on click, not on hover; resume commands run off the main thread (first-time Terminal permission no longer freezes the app).

### Changed
- Native English copy: "Got it", unified session terminology, "Show on" for screen choice.
- Localization tables cleaned (59 dead keys removed); release CI now runs the test suites before building.

## [1.3.2] - 2026-07-04

### Fixed
- Subagent storm: dozens of orchestrated child sessions finishing no longer queue dozens of alarm popups — bursts collapse to one alarm for the newest turn, the rest are recorded silently.

### Changed
- Faster alerts: FSEvents latency 0.2s → 0.05s, scan throttle 1s → 0.5s, confirm buffer 2.5s → 1s. A finished turn now pops its alarm in ~1.2-1.7s.

## [1.3.1] - 2026-07-03

### Fixed
- Turn alarms are reliable: per-turn delivery keys (no more repeat pop-ups from metadata writes), Claude Desktop's post-turn bookkeeping no longer randomly swallows "your turn", confirm re-checks against a fresh scan, and alarms auto-dismiss once you reply in the thread.
- Alarm window: first click acts even when unfocused, Esc closes, minimize removed (no stranded ringing panel), fixed size.
- A stalled session no longer masks a finished turn; acknowledged turns stop pinning the logo so a running sibling spins again.
- Usage-API errors (rate limit/offline) no longer silently swallow turn alarms.

### Added
- Event-driven scanning (FSEvents): the logo starts/stops with the run within ~1s; alarms land ~3s after a turn finishes.
- Two alarms queue instead of silently replacing each other; dismissing one recalls the next.
- "Open thread" on Claude Desktop sessions deep-links via claude://resume; several finished turns each get their own reminder.

### Changed
- Settings wording: width is now explained by Mac type (Notch / No notch); auto-resume naming unified (自动续跑) to match the README.
- verify.sh smoke launch runs in demo mode so it can never fire a real resume or touch the keychain.

## [0.1.4] - 2026-05-09

A polish + hardening release. One user-visible fix in Settings; the rest
is interior work — perf, refactor, and three release-pipeline guardrails
that exist so a botched future release doesn't silently brick auto-update.

### Fixed

- **Settings → Providers now shows auth errors instead of `0%`.** When
  Claude or Codex can't be reached (auth missing, expired, rate-limited),
  the row used to render `synced 2m ago · 0% / 0%` — the most authoritative
  diagnostic surface in the app silently masked the real reason. It now
  shows `⚠ auth required — run claude` (or whichever error fired) in place
  of the `0%`, per window.

### Internal

- **`IslandRootView` decomposed.** The root view used to observe seven
  stores; any `@Published` emission re-evaluated the whole tree, including
  every overlay and gesture closure. Split into `GlowLayer`, `LogoOverlay`,
  and `PeekPillOverlay` children, each subscribed to only what they read.
  Up to 8 redundant body re-evals per poll cycle eliminated.
- **`AppEnvironment` centralizes mode flags.** `CODEXISLAND_DEMO` /
  `CODEXISLAND_DEBUG` were checked across eight files via raw
  `ProcessInfo.processInfo.environment["..."]` lookups. Resolved once at
  launch into a typed enum (`AppEnvironment.isDemo`, `.isDebug`); a typo in
  any one literal can no longer silently miss the mode.
- **Generic `LogParseCache<Event>` shared by both log readers.**
  `ClaudeLogReader` and `CodexLogReader` previously duplicated ~70-80% of
  their cache + file-walk scaffolding. Extracted to one generic. Net
  −218 LOC across the two reader files. As a behavioral side effect, the
  Codex reader now uses the same 64 KB chunked streaming reader as Claude,
  closing a peak-RSS spike during 30-day rollout scans. Cache JSON shape
  is byte-identical, so existing caches survive the upgrade.

### Release pipeline

These all guard against silent bricks of Sparkle auto-update or the
Homebrew cask. None affect the running app — but if any one of them ever
fires, you'll get a loud failure at release time instead of a silently
broken update channel weeks later.

- **`build.sh` and `release.sh` reject non-semver `VERSION`.** A
  `VERSION` of `1` or `1.0` parses as `[1]` under Apple's component-wise
  comparator, which is *larger* than `0.0.99` — Sparkle would never offer
  any update to the affected installs. Tagging now fails loud at
  `error: VERSION must be X.Y.Z`.
- **`release.sh` aborts on empty EdDSA signature.** `set -euo pipefail`
  doesn't catch a zero-exit with malformed `sign_update` output. An
  appcast with `sparkle:edSignature=""` is rejected silently by every
  Sparkle client. The release now fails before the appcast is written.
- **CI uses an explicit DMG path for SHA-256.** A glob that matched no
  files would silently produce an empty SHA, which then `sed`'d into the
  Homebrew cask without changing it — `brew install` mismatched on every
  user. The path is now derived from the tag and existence-checked.
- **`build.sh` propagates Sparkle XPC codesign failures.** Previously
  swallowed via `2>/dev/null || true`, surfacing only at the user's first
  Check Now click as "The updater failed to start." The path-existence
  guard kept the original "tolerate missing helpers" behavior; real
  signing errors now fail the build.

## [0.1.0] - 2026-05-05

Three changes on top of the 0.0.10 baseline. The minor-version bump signals
that the 0.0.x bootstrap series is over — not that this single release is
big. Per-tag detail for the 0.0.x series lives on the
[GitHub Releases page](https://github.com/ericjypark/codex-island/releases).

### Added

- **Token counting toggle.** Settings → Providers → Tokens picks between
  *All tokens* (input + output + cache_creation + cache_read — ccusage
  parity, the prior default and the only mode in 0.0.x) and *Input + output*
  (matches Anthropic's claude.ai stats panel, which excludes cache reads).
  Both totals are computed every scan and cached, so flipping the segment
  is instant — no rescan.
- **`CHANGELOG.md`.** Going forward, each release ships with a curated
  user-facing changelog in this file.

### Changed

- **Continuous (squircle) corners on the island silhouette.** Replaces the
  hand-rolled circular-arc + straight-line path with
  `UnevenRoundedRectangle(style: .continuous)`, eliminating the small kink
  at the tangent point that was visible against the hardware notch.
- **Peek pill always shows window context.** When a provider didn't return
  an active `resetAt`, the pill used to drop the separator and render bare
  percentage — making the layout shift between hovers. It now always renders
  `<percent> · <label>`. With an active countdown the label is the live time
  remaining at full opacity; otherwise it falls back to the window length
  (`5h`) at reduced opacity, so countdown vs. passive label stays visually
  distinct without changing geometry.

### Internal

- `MacIsland.costCache.v2` → `v3`. First launch on 0.1.0 backfills the
  billable-tokens column with one fresh local-log scan; existing dollar +
  total-tokens rollups remain valid.

## [1.2.x] - 2026-06-28 → 2026-07-02

Reliability series for the turn alarm and quota display: stale-alarm suppression (metadata writes no longer resurrect old turns), immediate turn-state transitions, alarm control polish, quota display fixes, localization and peek-pill layout polish, Sparkle key rotation (1.2.2) and default feed URL fix.

## [1.1.0] - 2026-06-24

Visible Claude re-auth flow, security policy, Product Hunt / awesome-list badges, install-path docs for Chinese users.

## [1.0.0] - 2026-06-21

First public release: usage island (Claude + Codex 5h/weekly usage, cost, reset countdowns in the notch), live logo states (working / your turn / stalled), auto-resume triggers after quota reset, demo mode for filming, bilingual UI, launch video and screenshots.
