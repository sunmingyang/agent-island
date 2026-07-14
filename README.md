<div align="center">

<img src="Assets/agent-island-logo.png" alt="Agent Island logo" width="110">

# Agent Island

**A status companion for Claude Code and Codex — it lives in your notch.**

**[agent-island.dev](https://agent-island.dev)** · [简体中文](README.zh-CN.md)

[![Latest release](https://img.shields.io/github/v/release/tristan666666/agent-island?style=flat-square&color=0969da)](https://github.com/tristan666666/agent-island/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/tristan666666/agent-island/total?style=flat-square&color=10b981)](https://github.com/tristan666666/agent-island/releases)
[![macOS 13+](https://img.shields.io/badge/macOS-13%2B%20·%20Apple%20Silicon%20%26%20Intel-black?style=flat-square)](https://github.com/tristan666666/agent-island/releases/latest)
[![Windows 10+](https://img.shields.io/badge/Windows-10%2B%20·%20native%20WPF-0078d4?style=flat-square)](https://github.com/tristan666666/agent-island/releases/latest)
[![License](https://img.shields.io/github/license/tristan666666/agent-island?style=flat-square&color=8b5cf6)](LICENSE)

[![Listed in Chinese Independent Developer Projects](https://img.shields.io/badge/listed%20in-Chinese%20Independent%20Developer%20Projects-c2410c?style=flat-square)](https://github.com/1c7/chinese-independent-developer/pull/1085/files)
[![Listed in awesome-mac](https://img.shields.io/badge/listed%20in-awesome--mac-0969da?style=flat-square)](https://github.com/jaywcjlove/awesome-mac/blob/master/README.md#menu-bar-tools)
[![Listed in awesome-swift-macos-apps](https://img.shields.io/badge/listed%20in-awesome--swift--macOS-f97316?style=flat-square)](https://github.com/jaywcjlove/awesome-swift-macos-apps/blob/main/README.md#ai)
[![Listed in awesome-codex-cli](https://img.shields.io/badge/listed%20in-awesome--codex--cli-10b981?style=flat-square)](https://github.com/milisp/awesome-codex-cli)
[![Listed in awesome-coding-agents](https://img.shields.io/badge/listed%20in-awesome--coding--agents-7c3aed?style=flat-square)](https://github.com/kailiu42/awesome-coding-agents)
[![Listed in awesome-claude-code-and-skills](https://img.shields.io/badge/listed%20in-awesome--claude--code--and--skills-8b5cf6?style=flat-square)](https://github.com/GetBindu/awesome-claude-code-and-skills)
[![Listed in awesome-vibe-coding-resources](https://img.shields.io/badge/listed%20in-awesome--vibe--coding--resources-ec4899?style=flat-square)](https://github.com/acvnace/awesome-vibe-coding-resources#desktop-apps)

<a href="https://www.producthunt.com/products/agent-island-2?embed=true&utm_source=badge-featured&utm_medium=badge&utm_campaign=badge-agent-island-2">
  <img src="https://api.producthunt.com/widgets/embed-image/v1/featured.svg?post_id=1175477&theme=light" alt="Agent Island - status companion for Claude Code and Codex | Product Hunt" width="250" height="54">
</a>

<img src="docs/media/launch.gif" alt="Agent Island 1.6.1 launch film: weekly and monthly report cards, island ranks" width="900">

<sub><a href="https://github.com/tristan666666/agent-island/blob/main/docs/media/agentisland-1.6.1-launch-en.mp4">▶&nbsp;HD version</a></sub>

<p>
  <a href="#install"><strong>Install</strong></a> ·
  <a href="https://agent-island.dev">Website</a> ·
  <a href="https://github.com/tristan666666/agent-island/releases/latest">Latest release</a> ·
  <a href="docs/roadmap.md">Roadmap</a> ·
  <a href="CONTRIBUTING.md">Contribute</a>
</p>

<p><strong>If Agent Island saves you one stalled overnight Claude/Codex run, star it so more people running these agents — on Mac or Windows — can find it.</strong></p>

</div>

## Install

```sh
brew install tristan666666/tap/agentisland
```

**Windows**:

Download [`AgentIsland-win-x64.zip`](https://github.com/tristan666666/agent-island/releases/latest) from the latest release, unzip, and run `AgentIsland.exe`.

Or via Scoop:

```powershell
scoop bucket add agent-island https://github.com/tristan666666/scoop-bucket
scoop install agent-island/agentisland
```

Or via winget:

```powershell
winget install TristanTang.AgentIsland
```

or grab the DMG:

Download the DMG, drag AgentIsland into Applications, open it:

[**Download the latest AgentIsland.dmg**](https://github.com/tristan666666/agent-island/releases/latest)

If macOS blocks the first launch because the app is not notarized, right-click AgentIsland in Finder and choose **Open** once.

Or build from source:

```sh
git clone https://github.com/tristan666666/agent-island.git
cd agent-island
./scripts/verify.sh
open build/AgentIsland.app
```

## Features

### 🖥️ One app, native on macOS and Windows

Same product, same detection engine, no Electron. English and 简体中文, switchable in Settings.

- **macOS 13+** (SwiftUI, universal binary): wide top bar for notched MacBooks, compact top bar for everything else.
- **Windows 10/11** (WPF): top bar or a draggable floating widget, with a tray icon showing a usage ring at all times.

### ⚡ Live status, one glance up

<img src="Assets/agent-island-bar-working.png" alt="Notch bar with the Claude logo spinning while a session runs" width="760">

In your menu bar (macOS) or top bar (Windows), the Claude and Codex logos mirror what your sessions are actually doing. Detection is event-driven (FSEvents on the local transcript files), so the spin starts and stops within about a second of the run itself — no polling lag.

| Cue | Meaning |
|---|---|
| Logo **rotates** | a session is working |
| Logo **still** | nothing running — or the turn is over and it's yours |
| Logo **pulses red** | needs attention: rate limit, login, network, or provider error |

<img src="Assets/agent-island-bar-alert.png" alt="Notch bar with the Claude logo pulsing red" width="760">

### 📊 Usage island

Live usage, cost, and reset countdowns appear on swipeable pages in the notch, fed by each provider's own usage API. Claude shows 5-hour and weekly usage.
Codex shows weekly usage. When Claude's endpoint demands a fresh login, the Re-authenticate button finishes it in your browser (one click on the real claude.com authorize page, caught by a local callback) — no terminal, no code pasting.

<img src="Assets/agent-island-usage.png" alt="Usage page with Claude and Codex windows, cost, and reset countdowns" width="760">

**Reset bank**: an ×N chip shows your banked Codex resets — click it for each card's expiry.

### 🏆 Weekly & monthly report cards — new in 1.6.1

One click renders your week (or your half-year) into a shareable card: total tokens with an "≈ API value" line, the Claude/Codex split, a TOP-5 model breakdown (tokens · dollars · share), a 24-week activity heatmap with your streak — and your **island rank**, seven lifetime tiers from 🌊 Drifter (100M) to 👑 Legendary Navigator (100B). Rendered entirely on your machine; you copy the image and post it yourself.

<table>
  <tr>
    <td align="center"><img src="Assets/report-weekly-en.png" alt="Weekly report card: tokens, API value, provider split, TOP-5 model donut, island rank" width="380"></td>
    <td align="center"><img src="Assets/report-monthly-en.png" alt="Monthly report card: 24-week activity heatmap with streak and island rank" width="380"></td>
  </tr>
</table>

### 🔔 "It's your turn" alarms

A turn finishes in a background session → an alarm window, a system notification, and a sound, within seconds.

- **Reply and it goes away**; multiple finishes queue instead of swallowing each other.
- **Quota counts too** — a maxed Claude window gets one separate alarm with its real reset time.

<table>
  <tr>
    <td align="center"><img src="Assets/agent-island-turn-alarm-claude.png" alt="Turn alarm for a finished Claude thread" width="420"></td>
    <td align="center"><img src="Assets/agent-island-turn-alarm-codex.png" alt="Turn alarm for a finished Codex thread" width="420"></td>
  </tr>
</table>

### 🎨 Also in the box: five dials

⌘-click the island to cycle chart styles; flip used ↔ remaining anytime.

<img src="Assets/chart-styles-en.png" alt="Five chart styles" width="760">

## How it works

- **Session state** is read from the transcript files Claude Code, Claude Desktop, and Codex already write on your disk: FSEvents watches for writes, and end-of-turn markers (Claude's `stop_reason: end_turn`, Codex's `task_complete`) plus file activity decide spin / alarm / red.
- **Usage and reset times** come from each provider's real usage API, using the credentials already on your machine.
- Everything runs locally as you. Nothing is uploaded anywhere.

Deeper write-up: [How Agent Island detects Claude Code and Codex session state](docs/how-agent-island-detects-session-state.md).

## FAQ & safety

**Why isn't the app notarized?**
No paid Apple Developer account. The app is ad-hoc signed, so macOS asks for one right-click → Open on first launch. Auto-updates are independently verified: Sparkle checks every update against an EdDSA signature before installing.

**Does any of my data leave my Mac?**
No. Agent Island reads local transcript files and calls the providers' usage APIs with your existing local tokens. There is no telemetry in the app.

**How is this different from codex-island?**
[codex-island](https://github.com/ericjypark/codex-island) is a passive usage meter — it shows how much you've used. Agent Island keeps that (usage, cost, resets) and adds the active half: live session state on the logos and turn alarms.

## WeChat Community (中文交流群)

Chinese-speaking users — scan to join our WeChat group:

<img src="Assets/wechat-qr.jpg" alt="WeChat group QR — add the author, note Agent Island" width="300">

## Credits & license

Agent Island is a fork of **[codex-island](https://github.com/ericjypark/codex-island)** by **Eric Park** — the usage-island and cost-tracking foundation are his work. Agent Island adds turn alarms, live session-state animations, cross-platform support, and its own product direction.

MIT licensed — © 2026 Eric Park. This fork retains that notice. See [LICENSE](LICENSE).
