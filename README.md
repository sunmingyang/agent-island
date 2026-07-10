<div align="center">

<img src="Assets/agent-island-logo.png" alt="Agent Island logo" width="110">

# Agent Island

**A status companion for Claude Code and Codex.**

It lives in your MacBook's notch. Spinning logo = agent working. Alarm = your turn. Red = something needs you.

Works with both notched and non-notched Macs: use the notch-style top bar on MacBooks, or the compact top bar on external displays, iMac, Mac mini, and older MacBooks.

**[agent-island.dev](https://agent-island.dev)** · [简体中文](README.zh-CN.md)

[![Latest release](https://img.shields.io/github/v/release/tristan666666/agent-island?style=flat-square&color=0969da)](https://github.com/tristan666666/agent-island/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/tristan666666/agent-island/total?style=flat-square&color=10b981)](https://github.com/tristan666666/agent-island/releases)
[![macOS 13+](https://img.shields.io/badge/macOS-13%2B%20·%20Apple%20Silicon%20%26%20Intel-black?style=flat-square)](https://github.com/tristan666666/agent-island/releases/latest)
[![License](https://img.shields.io/github/license/tristan666666/agent-island?style=flat-square&color=8b5cf6)](LICENSE)

[![Listed in awesome-mac](https://img.shields.io/badge/listed%20in-awesome--mac-0969da?style=flat-square)](https://github.com/jaywcjlove/awesome-mac/blob/master/README.md#menu-bar-tools)
[![Listed in awesome-swift-macos-apps](https://img.shields.io/badge/listed%20in-awesome--swift--macOS-f97316?style=flat-square)](https://github.com/jaywcjlove/awesome-swift-macos-apps/blob/main/README.md#ai)
[![Listed in awesome-codex-cli](https://img.shields.io/badge/listed%20in-awesome--codex--cli-10b981?style=flat-square)](https://github.com/milisp/awesome-codex-cli)
[![Listed in awesome-coding-agents](https://img.shields.io/badge/listed%20in-awesome--coding--agents-7c3aed?style=flat-square)](https://github.com/kailiu42/awesome-coding-agents)
[![Listed in awesome-claude-code-and-skills](https://img.shields.io/badge/listed%20in-awesome--claude--code--and--skills-8b5cf6?style=flat-square)](https://github.com/GetBindu/awesome-claude-code-and-skills)
[![Listed in awesome-vibe-coding-resources](https://img.shields.io/badge/listed%20in-awesome--vibe--coding--resources-ec4899?style=flat-square)](https://github.com/acvnace/awesome-vibe-coding-resources#desktop-apps)

<a href="https://www.producthunt.com/products/agent-island-2?embed=true&utm_source=badge-featured&utm_medium=badge&utm_campaign=badge-agent-island-2">
  <img src="https://api.producthunt.com/widgets/embed-image/v1/featured.svg?post_id=1175477&theme=light" alt="Agent Island - status companion for Claude Code and Codex | Product Hunt" width="250" height="54">
</a>

<video src="https://github.com/user-attachments/assets/d69b41e0-9298-4f17-b6c9-6014f3bd956b" controls width="900"></video>

<p>
  <a href="#install"><strong>Install</strong></a> ·
  <a href="https://agent-island.dev">Website</a> ·
  <a href="https://github.com/tristan666666/agent-island/releases/latest">Latest release</a> ·
  <a href="docs/roadmap.md">Roadmap</a> ·
  <a href="CONTRIBUTING.md">Contribute</a>
</p>

<p><strong>If Agent Island saves you one stalled overnight Claude/Codex run, star it so more Mac users can find it.</strong></p>

<table>
  <tr>
    <td align="center"><img src="Assets/agent-island-turn-alarm-claude.png" alt="Turn alarm for a finished Claude thread" width="420"></td>
    <td align="center"><img src="Assets/agent-island-turn-alarm-codex.png" alt="Turn alarm for a finished Codex thread" width="420"></td>
  </tr>
  <tr>
    <td align="center" colspan="2"><img src="Assets/agent-island-bar-working.png" alt="Notch bar with the Claude logo spinning while a session runs" width="760"></td>
  </tr>
</table>

</div>

## Why

Agent Island was built by a heavy Claude Code + Codex user, out of two everyday realities:

**Squeeze every token.** Running both tools daily means juggling two quota clocks — when does the 5-hour window reset, which tool has headroom right now. Cross-using them well is how you get the most out of what you pay for. So: live usage, cost, and reset countdowns for both, one glance in the top bar.

**Start the run, then go live your life.** You brief the agent for one round — then you should be able to walk away: play with your kids, hit the gym, watch a movie. The agent doesn't need you while it runs; it needs you when it stops. Agent Island watches for that exact moment — rings you when it's your turn, resumes chosen sessions when the quota comes back — so coding doesn't own your evening.

That's the whole point: maximum throughput from the machines, and your life back from the loop.

## Features

### ⚡ Live status in the top bar

The Claude and Codex logos mirror what your sessions are actually doing. Detection is event-driven (FSEvents on the local transcript files), so the spin starts and stops within about a second of the run itself — no polling lag.

| Cue | Meaning |
|---|---|
| Logo **rotates** | a session is working |
| Logo **still** | nothing running — or the turn is over and it's yours |
| Logo **pulses red** | needs attention: rate limit, login, network, or provider error |

<img src="Assets/agent-island-bar-alert.png" alt="Notch bar with the Claude logo pulsing red" width="760">

### 🖥️ Notched and non-notched Mac layouts

Agent Island is not limited to MacBooks with a camera notch. In Settings you can choose:

- **Wide top bar** for MacBook notch-style layouts.
- **Compact top bar** for non-notched Macs, external displays, iMac, Mac mini, and older MacBooks.

The app still runs as a lightweight native top-bar companion either way.

### 🔔 "It's your turn" alarms

When a turn finishes in a background session, Agent Island opens a foreground alarm window, posts a system notification, and plays a sound (built-in choices, or bring your own file). Alarms land a few seconds after the turn actually ends.

- **Reply and it goes away** — the alarm auto-dismisses once you answer in the thread; no stale windows.
- **Nothing gets swallowed** — if several turns finish, alarms queue; dismissing one recalls the next.
- **Open thread** jumps back to the exact session: Claude Desktop via its `claude://resume` deep link, the Codex app via `codex://threads/…`, and CLI sessions by reopening the resume command in your terminal.

The screenshots at the top are this alarm, one per provider.

### 🔁 Auto-resume when the quota resets

Attach a rule to a session: when the provider's usage window resets — or on a fixed every-N-hours schedule — Agent Island sends it a message (`continue`, `OK`, whatever you set) so overnight work picks itself back up.

<img src="Assets/agent-island-auto-trigger.png" alt="Auto-resume rules page in the island" width="760">

Built-in guardrails, because this feature runs unattended:

- A **kill switch** in Settings — when off, no resume command is ever spawned.
- A **per-project allow list** — resume only fires for directories you explicitly allow.
- **Records** — every run, executed or blocked, is logged; open the folder from Settings.

Honest limits: your Mac must be awake, and every resumed run spends tokens. See [FAQ & safety](#faq--safety) for what the `--dangerously-*` flags mean.

### 📊 Usage island

Live Claude & Codex 5-hour and weekly usage, cost, and reset countdowns — swipeable pages in the notch, fed by each provider's own usage API.

<img src="Assets/agent-island-usage.png" alt="Usage page with Claude and Codex windows, cost, and reset countdowns" width="760">

### 🌏 Native and bilingual

Native SwiftUI — no Electron. English and 简体中文, switchable in Settings. macOS 13+, universal binary (Apple Silicon + Intel).

Windows is here too: [Agent Island for Windows](windows/) is a native WPF port with the same detection engine. More platform notes on [agent-island.dev](https://agent-island.dev).

<!-- launch video: coming soon -->

## Install

> **On Windows?** Grab [**Agent Island for Windows**](windows/) — a native WPF port with the same detection engine ([download](https://github.com/tristan666666/agent-island/releases/latest)).

```sh
brew install tristan666666/tap/agentisland
```

**Windows** (new):

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

## How it works

- **Session state** is read from the transcript files Claude Code, Claude Desktop, and Codex already write on your disk: FSEvents watches for writes, and end-of-turn markers (Claude's `stop_reason: end_turn`, Codex's `task_complete`) plus file activity decide spin / alarm / red.
- **Usage and reset times** come from each provider's real usage API, using the credentials already on your machine.
- **Auto-resume** runs `claude --resume … -p "<msg>" --dangerously-skip-permissions` or `codex exec resume … "<msg>" --dangerously-bypass-approvals-and-sandbox`. Run records land in `~/Library/Application Support/AgentIsland/trigger-runs/`.
- Everything runs locally as you. Nothing is uploaded anywhere.

Deeper write-up: [How Agent Island detects Claude Code and Codex session state](docs/how-agent-island-detects-session-state.md).

## FAQ & safety

**What do the `--dangerously-*` resume flags actually do?**
They restore the agent *unattended, with permission checks off* — that's the only way a session can continue without a human clicking "allow". Treat it accordingly: the kill switch, the per-project allow list, and the run records exist exactly so you can scope this to sessions you trust. Only attach rules to work you'd be comfortable running unattended.

**Why isn't the app notarized?**
No paid Apple Developer account. The app is ad-hoc signed, so macOS asks for one right-click → Open on first launch. Auto-updates are independently verified: Sparkle checks every update against an EdDSA signature before installing.

**Does any of my data leave my Mac?**
No. Agent Island reads local transcript files and calls the providers' usage APIs with your existing local tokens. There is no telemetry in the app.

**How is this different from codex-island?**
[codex-island](https://github.com/ericjypark/codex-island) is a passive usage meter — it shows how much you've used. Agent Island keeps that (usage, cost, resets) and adds the active half: live session state on the logos, turn alarms, and auto-resume.

## Credits & license

Agent Island is a fork of **[codex-island](https://github.com/ericjypark/codex-island)** by **Eric Park** — the usage-island and cost-tracking foundation are his work. Agent Island adds auto-resume, turn alarms, live session-state animations, and its own product direction.

MIT licensed — © 2026 Eric Park. This fork retains that notice. See [LICENSE](LICENSE).

## Star History

[![Star History Chart](https://api.star-history.com/svg?repos=tristan666666/agent-island&type=Date)](https://star-history.com/#tristan666666/agent-island&Date)
