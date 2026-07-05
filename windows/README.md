<div align="center">

<img src="src/AgentIsland/Assets/agentisland_logo.png" alt="Agent Island logo" width="110">

# Agent Island for Windows

**A status companion for Claude Code and Codex.**

A floating island at the top of your screen. Spinning logo = agent working. Alarm = your turn. Red = something needs you.

The Windows port of [Agent Island for macOS](https://github.com/tristan666666/agent-island) — same detection engine, same look, same promise: **start the run, go live your life.**

</div>

## Features

### ⚡ Live status in the top bar
The Claude and Codex logos mirror what your sessions are actually doing. Detection is event-driven (file watchers on the local transcript files), so the spin starts and stops within about a second of the run itself.

| Cue | Meaning |
|---|---|
| Logo **rotates** | a session is working |
| Logo **still** | nothing running — or the turn is over and it's yours |
| Logo **pulses red** | needs attention: rate limit, login, network, or provider error |

### 🔔 "It's your turn" alarms
When a turn finishes in a background session, Agent Island opens a foreground alarm window, posts a notification, and plays a sound. Reply in the thread and it dismisses itself; multiple finished turns queue instead of stacking. **Open session** jumps back via the `claude://` deep link, or reopens the resume command in a terminal for CLI sessions.

### 🔁 Auto-resume when the quota resets
Attach a rule to a session: when the provider's usage window resets — or on a fixed every-N-hours schedule — Agent Island sends it your message so overnight work picks itself back up. Guardrails: a **kill switch**, a **per-project allow list**, and **run records** for every fire, executed or blocked.

### 📊 Usage, cost, and history
Live Claude & Codex 5-hour and weekly windows with reset countdowns (each provider's own usage API), local-log cost tracking with a daily sparkline, and a year-to-date contribution grid — swipeable pages in the island.

### 🌏 Native and bilingual
Native WPF — no Electron. English and 简体中文. Approaching-limit alerts (amber at 80%, red at 95%, opt-in), configurable alarm sounds, launch at login.

## Install

Grab the zip from Releases, unpack, run `AgentIsland.exe`. Or build from source:

```powershell
git clone <this-repo>
cd AgentIsland
.\build.ps1          # → dist\AgentIsland-<version>-win-x64.zip
```

Requires Windows 10/11. The self-contained build needs no .NET runtime install.

## How it works

Everything runs locally as you. Nothing is uploaded anywhere.

- **Session state** is read from the transcript files Claude Code, Claude Desktop, and Codex already write:
  - `%USERPROFILE%\.claude\projects\**\*.jsonl` (Claude Code transcripts)
  - `%APPDATA%\Claude\claude-code-sessions\**\local_*.json` (Claude Desktop session store — titles, archived flags)
  - `%USERPROFILE%\.codex\sessions\**\*.jsonl` (Codex rollouts)
  
  End-of-turn markers (Claude's `stop_reason: end_turn`, Codex's `task_complete`) plus file activity decide spin / alarm / red.
- **Usage and reset times** come from each provider's real usage API, using the credentials already on your machine (`%USERPROFILE%\.claude\.credentials.json`, `%USERPROFILE%\.codex\auth.json`). Refreshed OAuth tokens are rotated back into the credentials file, exactly like Claude Code itself does.
- **Cost** is computed from the same local logs with a hardcoded price table (ccusage parity; unknown models price at $0 and are flagged).
- **Auto-resume** runs `claude --resume … -p "<msg>" --dangerously-skip-permissions` or `codex exec resume … --dangerously-bypass-approvals-and-sandbox`. Those flags bypass approval prompts — that's what unattended resume means. Only attach rules to projects you trust; run records land in `%APPDATA%\AgentIsland\trigger-runs\`.

## Where things live

| What | Where |
|---|---|
| Settings | `%APPDATA%\AgentIsland\settings.json` |
| Auto-resume run records | `%APPDATA%\AgentIsland\trigger-runs\` |
| Log parse caches | `%LOCALAPPDATA%\AgentIsland\cache\` |
| Crash log | `%APPDATA%\AgentIsland\crash.log` |

## Development

```powershell
dotnet build src\AgentIsland\AgentIsland.csproj      # debug build
dotnet build tests\AgentIsland.Tests\AgentIsland.Tests.csproj
.\tests\AgentIsland.Tests\bin\Debug\net8.0-windows\AgentIsland.Tests.exe   # 19 contract tests
.\tests\AgentIsland.Tests\bin\Debug\net8.0-windows\AgentIsland.Tests.exe scan  # live session scan
```

Demo mode for screenshots: set `AGENTISLAND_DEMO=1` (synthetic healthy data; auto-resume never fires).

The test suites are 1:1 ports of the macOS contract tests (SessionTurnState, UsageCachePolicy, ReminderDeliveryKey), so the two implementations stay behaviorally aligned.

## Credits & license

Windows port of **[Agent Island](https://github.com/tristan666666/agent-island)**, which is a fork of **[codex-island](https://github.com/ericjypark/codex-island)** by Eric Park. MIT licensed — © 2026 Eric Park; port © 2026 Agent Island contributors.

---

<div align="center">

**任务跑起来，你去过生活。** 它盯着 Claude Code 和 Codex，轮到你时叫你。

</div>
