<div align="center">

<img src="Assets/agent-island-logo.png" alt="Agent Island logo" width="110">

# Agent Island

**A status companion for Claude Code and Codex.**

See what every run is doing. Step away, and Agent Island calls you back when it is your turn. Local-first, no Agent Island account, no product telemetry.

**[agent-island.dev](https://agent-island.dev)** · [简体中文](README.zh-CN.md)

[![Latest release](https://img.shields.io/github/v/release/tristan666666/agent-island?style=flat-square&color=0969da)](https://github.com/tristan666666/agent-island/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/tristan666666/agent-island/total?style=flat-square&color=10b981)](https://github.com/tristan666666/agent-island/releases)
[![Platforms](https://img.shields.io/badge/platforms-macOS%2013%2B%20%7C%20Windows%2010%2F11-24292f?style=flat-square)](#macos-and-windows)
[![Build](https://img.shields.io/github/actions/workflow/status/tristan666666/agent-island/macos-ci.yml?branch=main&style=flat-square&label=build)](https://github.com/tristan666666/agent-island/actions)
[![License](https://img.shields.io/github/license/tristan666666/agent-island?style=flat-square&color=8b5cf6)](LICENSE)

[![Listed in awesome-mac](https://img.shields.io/badge/listed%20in-awesome--mac-0969da?style=flat-square)](https://github.com/jaywcjlove/awesome-mac/blob/master/README.md#menu-bar-tools)
[![Listed in awesome-swift-macos-apps](https://img.shields.io/badge/listed%20in-awesome--swift--macOS-f97316?style=flat-square)](https://github.com/jaywcjlove/awesome-swift-macos-apps/blob/main/README.md#ai)
[![Listed in awesome-codex-cli](https://img.shields.io/badge/listed%20in-awesome--codex--cli-10b981?style=flat-square)](https://github.com/milisp/awesome-codex-cli)
[![Listed in awesome-coding-agents](https://img.shields.io/badge/listed%20in-awesome--coding--agents-7c3aed?style=flat-square)](https://github.com/kailiu42/awesome-coding-agents)
[![Listed in awesome-claude-code-and-skills](https://img.shields.io/badge/listed%20in-awesome--claude--code--and--skills-8b5cf6?style=flat-square)](https://github.com/GetBindu/awesome-claude-code-and-skills)
[![Listed in awesome-vibe-coding-resources](https://img.shields.io/badge/listed%20in-awesome--vibe--coding--resources-ec4899?style=flat-square)](https://github.com/acvnace/awesome-vibe-coding-resources#desktop-apps)
[![Listed in awesome-vibecoding](https://img.shields.io/badge/listed%20in-awesome--vibecoding-0ea5e9?style=flat-square)](https://github.com/roboco-io/awesome-vibecoding#projects-platforms--tools)
[![Listed in Chinese Independent Developer Projects](https://img.shields.io/badge/listed%20in-Chinese%20Independent%20Developer%20Projects-c2410c?style=flat-square)](https://github.com/1c7/chinese-independent-developer/pull/1085/files)

<a href="https://www.producthunt.com/products/agent-island-2?embed=true&utm_source=badge-featured&utm_medium=badge&utm_campaign=badge-agent-island-2">
  <img src="https://api.producthunt.com/widgets/embed-image/v1/featured.svg?post_id=1175477&theme=light" alt="Agent Island - status companion for Claude Code and Codex | Product Hunt" width="250" height="54">
</a>

<img src="docs/media/launch.gif" alt="Agent Island 1.6.1 launch film: weekly and monthly report cards, island ranks" width="900">

<sub><a href="https://github.com/tristan666666/agent-island/blob/main/docs/media/agentisland-1.6.1-launch-en.mp4">▶&nbsp;HD version</a></sub>

<!-- Swap the launch film above for the 8-12 second product demo loop (running -> your turn -> open session) once it exists. -->

<p>
  <a href="#quick-start"><strong>Quick Start</strong></a> ·
  <a href="https://github.com/tristan666666/agent-island/releases/latest">Download</a> ·
  <a href="https://agent-island.dev">Website</a> ·
  <a href="docs/how-agent-island-detects-session-state.md">How it works</a> ·
  <a href="CONTRIBUTING.md">Contribute</a>
</p>

</div>

## Quick Start

Choose your platform and install the current `v1.6.1` release directly:

| Platform | Recommended download | Requirement |
|---|---|---|
| macOS | [AgentIsland-1.6.1.dmg](https://github.com/tristan666666/agent-island/releases/download/v1.6.1/AgentIsland-1.6.1.dmg) | macOS 13+, Apple silicon or Intel |
| Windows | [AgentIsland-1.6.1-win-x64.zip](https://github.com/tristan666666/agent-island/releases/download/v1.6.1/AgentIsland-1.6.1-win-x64.zip) | Windows 10/11 x64 |

On macOS, drag Agent Island into Applications. The app is ad-hoc signed rather than notarized, so the first launch requires right-clicking the app in Finder and choosing **Open**.

On Windows, unzip the archive and run `AgentIsland.exe`.

<details>
<summary>Package managers and source builds</summary>

Homebrew, WinGet, and Scoop may lag behind the latest GitHub release. Check the version they offer before installing.

```sh
brew install tristan666666/tap/agentisland
```

```powershell
winget install TristanTang.AgentIsland
```

```powershell
scoop bucket add agent-island https://github.com/tristan666666/scoop-bucket
scoop install agent-island/agentisland
```

Build the macOS app from source:

```sh
git clone https://github.com/tristan666666/agent-island.git
cd agent-island
./scripts/verify.sh
open build/AgentIsland.app
```

Windows build and test instructions are tracked in [issue #10](https://github.com/tristan666666/agent-island/issues/10).

</details>

## Table of Contents

- [Features](#features)
  - [Status monitoring](#status-monitoring)
  - [It's-your-turn clock](#its-your-turn-clock)
  - [Usage & reports](#usage--reports)
  - [macOS and Windows](#macos-and-windows)
- [How it works](#how-it-works)
- [Community](#community)
- [Why Agent Island](#why-agent-island)
- [Privacy and safety](#privacy-and-safety)
- [FAQ](#faq)
- [Contributing](#contributing)
- [Roadmap and releases](#roadmap-and-releases)
- [Credits and license](#credits-and-license)

## Features

### Status monitoring

Agent Island mirrors local Claude Code, Claude Desktop, and Codex session activity in a compact top bar. You can scan the state without bringing each session to the foreground.

<img src="Assets/agent-island-bar-working.png" alt="Agent Island showing an active Claude session in the macOS top bar" width="760">

| Cue | Meaning |
|---|---|
| Logo rotates | A session is working |
| Logo is still | No session is currently working |
| Logo pulses red | A session needs attention because of a provider, login, network, or rate-limit error |

<img src="Assets/agent-island-bar-alert.png" alt="Agent Island showing an attention state in the macOS top bar" width="760">

### It's-your-turn clock

When a background turn finishes, Agent Island can show an alarm window, send a system notification, and play a sound. Multiple completed turns queue instead of replacing one another, and responding clears the corresponding reminder.

<table>
  <tr>
    <td align="center"><img src="Assets/agent-island-turn-alarm-claude.png" alt="Your-turn alert for a completed Claude session" width="420"></td>
    <td align="center"><img src="Assets/agent-island-turn-alarm-codex.png" alt="Your-turn alert for a completed Codex session" width="420"></td>
  </tr>
</table>

### Usage & reports

Swipe through local usage and cost views for Claude and Codex. Provider usage data comes from provider-owned usage endpoints through the local credential store; cost and model summaries are calculated locally from session records.

<img src="Assets/agent-island-usage.png" alt="Agent Island usage view for Claude and Codex on macOS" width="760">

Agent Island also renders weekly and longer-range report cards on your machine. Copying or sharing a card is an explicit user action; Agent Island does not publish it for you.

<!-- OPTIMIZED_REPORT_SCREENSHOTS_PLACEHOLDER
Add README-sized WebP report examples here after export and visual review.
Do not restore the multi-megabyte PNG pair to the README.
-->

### macOS and Windows

Agent Island is a native desktop app on both supported platforms, with English and Simplified Chinese interfaces.

- **macOS 13+**: SwiftUI universal app for Apple silicon and Intel, with wide and compact top-bar layouts.
- **Windows 10/11 x64**: native WPF app with a top bar, draggable floating widget, and tray presence.

<!-- WINDOWS_SCREENSHOTS_PLACEHOLDER
Add verified Windows screenshots here only after capture and release-behavior review:
1. running / waiting top bar or floating widget;
2. your-turn alert;
3. usage or report view.
-->

<!-- PLATFORM_CAPABILITY_MATRIX_PLACEHOLDER
Add the macOS / Windows capability matrix only after the current release has been verified on both platforms.
Do not infer parity from release notes or CI alone.
-->

## How it works

```mermaid
flowchart LR
    A[Claude and Codex local files] --> B[Local parser and state machine]
    B --> C[Top bar and alerts]
    B --> D[Local cost and report views]
    E[Provider-owned usage endpoints] --> D
```

- **Session state** comes from transcript and activity files that Claude Code, Claude Desktop, and Codex already write to disk. Local file events and turn markers drive the working and needs-you states.
- **Usage and reset data** comes from provider-owned usage endpoints through the local credential store.
- **Cost, model, and report summaries** are calculated locally from local session records.

Read the implementation overview: [How Agent Island detects Claude Code and Codex session state](docs/how-agent-island-detects-session-state.md).

## Community

Chinese-speaking users — scan the group QR to join directly. Group codes rotate every 7 days; if it has expired, add the author and mention "Agent Island" to be invited:

<table>
  <tr>
    <td align="center">
      <img src="Assets/wechat-group-qr.jpg" alt="WeChat group QR code for the Agent Island community" width="260"><br>
      <sub>WeChat group — scan to join</sub>
    </td>
    <td align="center">
      <img src="Assets/wechat-qr.jpg" alt="Author's WeChat QR code; mention Agent Island to be invited" width="260"><br>
      <sub>Group code expired? Add the author, mention "Agent Island"</sub>
    </td>
  </tr>
</table>

See Agent Island on [Product Hunt](https://www.producthunt.com/products/agent-island-2).

## Why Agent Island

Long Claude Code and Codex runs should not require keeping every terminal in view. Agent Island gives each provider a persistent status surface, tells you when a run needs attention, and brings you back when the next action is yours.

It is built for developers who:

- run Claude Code and Codex sessions in parallel;
- leave long tasks working in the background;
- want status, alerts, and usage views without sending session data to another service.

How it compares with its neighbors:

| | Agent Island | [Vibe Island](https://vibeisland.app) | [CodexBar](https://github.com/steipete/CodexBar) | [ccusage](https://github.com/ccusage/ccusage) | [Claude Code Usage Monitor](https://github.com/Maciek-roboblog/Claude-Code-Usage-Monitor) | [CCSeva](https://github.com/Iamshankhadeep/ccseva) | [codex-island](https://github.com/ericjypark/codex-island) |
|---|---|---|---|---|---|---|---|
| Price & source | Free · MIT | One-time purchase · closed | Free · MIT | Free · MIT | Free · MIT | Free · MIT | Free · MIT |
| Form | Menu-bar app | Notch app | Menu-bar app | CLI | Terminal dashboard | Menu-bar app | Menu-bar app |
| Platforms | macOS 13+ · Windows 10/11 | macOS 14+ | macOS 14+ (CLI also on Linux) | Anywhere Node runs | Anywhere Python runs | macOS | macOS |
| Agents | Claude Code · Codex | Claude Code, Codex, Gemini CLI, Cursor, and more | 59 providers (limits) | Claude Code (+ Codex) | Claude Code | Claude Code | Codex (+ Claude usage) |
| Live session status | ✓ | ✓ | — (provider incident badges) | — | — | — | — (passive usage meter) |
| Your-turn alarm window + sound + queue | ✓ | Done notice, click to jump | — | — | — | — | — |
| Out-of-quota alarm | ✓ | — | — | — | Terminal warnings | 70/90% threshold notifications | — |
| In-notch permission approvals | — | ✓ | — | — | — | — | — |
| Usage, cost & resets | ✓ (incl. reset bank) | Usage windows | ✓ (59 providers, reset countdowns, spend) | ✓ (local cost reports) | ✓ (real-time + predictions) | ✓ (5h/weekly gauges + countdowns) | ✓ (incl. reset credits) |
| Weekly/monthly report cards & ranks | ✓ | — | — | — | — | — | — |

<sub>Based on each product's public materials as of July 2026 — corrections welcome via issue.</sub>

## Privacy and safety

- No Agent Island account is required.
- Session data is not uploaded to Agent Island.
- The app has no product telemetry.
- Usage and authentication calls go directly to provider-owned endpoints through the local credential store.
- If you use Claude re-authentication, Agent Island may refresh and update credentials shared with Claude Code or Claude Desktop in that local store.
- macOS updates are verified by Sparkle with an EdDSA signature before installation.

Agent Island reads the local files and credentials required to provide these views. Review the source and release artifacts before installing, as you would for any local developer tool.

## FAQ

<details>
<summary><strong>Why is the macOS app not notarized?</strong></summary>

The project does not currently use a paid Apple Developer account. The macOS build is ad-hoc signed, so the first launch requires right-clicking Agent Island in Finder and choosing **Open**. Sparkle independently verifies update signatures before installation.

</details>

<details>
<summary><strong>Does session data leave my computer?</strong></summary>

Session state, cost calculations, and reports are derived locally. Agent Island does not upload session data or collect product telemetry. Usage views call provider-owned endpoints through the local credential store. Claude re-authentication may refresh and update credentials shared with Claude Code or Claude Desktop in that store.

</details>

<details>
<summary><strong>How is this different from codex-island?</strong></summary>

[codex-island](https://github.com/ericjypark/codex-island) established the usage-island and cost-tracking foundation. Agent Island builds on it with live session state, your-turn alerts, Windows support, and a broader desktop workflow.

</details>

## Contributing

Contributions are welcome across macOS, Windows, documentation, tests, and localization. Start with [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md).

Current good first issues:

- [#10: Document the Windows contributor build and test workflow](https://github.com/tristan666666/agent-island/issues/10)
- [#11: Add a localization key parity check](https://github.com/tristan666666/agent-island/issues/11)
- [#15: Add a public-copy guard for retired feature claims](https://github.com/tristan666666/agent-island/issues/15)

Run `./scripts/verify.sh` before opening a macOS pull request. Windows changes are checked by the repository's Windows CI workflow.

## Roadmap and releases

- [Latest release](https://github.com/tristan666666/agent-island/releases/latest)
- [Roadmap](docs/roadmap.md)
- [Open issues](https://github.com/tristan666666/agent-island/issues)

## Credits and license

Agent Island is a fork of **[codex-island](https://github.com/ericjypark/codex-island)** by **Eric Park**. The original usage-island and cost-tracking foundation are his work. Agent Island adds live session-state views, your-turn alerts, cross-platform support, and its own product direction.

MIT licensed. Copyright 2026 Eric Park. This fork retains the original notice. See [LICENSE](LICENSE).
