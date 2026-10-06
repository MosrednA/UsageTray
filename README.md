<p align="center">
  <img src="docs/logo.png" alt="" width="96">
</p>

<h1 align="center">UsageTray</h1>

<p align="center">
  <b>Your Codex and Claude subscription limits at a glance — and whether you're on pace to make it to the reset.</b>
</p>

<p align="center">
  <a href="https://github.com/MosrednA/UsageTray/releases/latest"><img src="https://img.shields.io/github/v/release/MosrednA/UsageTray?style=flat-square&color=4cc38a" alt="Latest release"></a>
  <a href="https://github.com/MosrednA/UsageTray/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/MosrednA/UsageTray/ci.yml?style=flat-square&label=build" alt="Build"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-1c1d22?style=flat-square" alt="Windows 10 | 11">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-1c1d22?style=flat-square" alt="MIT license"></a>
</p>

<p align="center">
  <img src="docs/flyout.png" alt="UsageTray flyout showing Codex and Claude limits" width="440">
</p>

A tiny native Windows tray app. One glance at the icon tells you if you're burning through a limit; one click shows every window with a pace marker, so you know whether to slow down — or that you've got room to spare.

- **Pace, not just percentage.** A white tick marks where you'd be with perfectly even usage. Past the tick means you're going too fast.
- **Codex + Claude in one place.** Weekly and 5-hour windows, model-specific Claude limits, Codex credits.
- **Zero setup for Codex.** Reads the logs Codex already writes. No keys, no login.
- **Lightweight.** Native WinForms, no dependencies, no telemetry. Refreshes every 2 minutes.

## Install

Download **[UsageTray-win-x64.exe](https://github.com/MosrednA/UsageTray/releases/latest/download/UsageTray-win-x64.exe)** and run it. Nothing to install.

> Prefer a ~250 KB download instead of ~50 MB? Grab `UsageTray-win-x64-small.exe` from the [release](https://github.com/MosrednA/UsageTray/releases/latest) instead — it needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

Then:

1. **Keep the icon visible** — drag it from the `^` overflow onto the taskbar (or *Taskbar settings → Other system tray icons → UsageTray*).
2. **Start with Windows** — right-click the icon and tick it.
3. **Claude** — if you see *sign-in expired*, click it (or right-click → *Sign in to Claude…*). This runs the official `claude auth login`.

## Reading it

```
7d   ███████████████▏░░░░░░░░   64%   +11   3d 6h
│    │              │           │     │     └─ time until reset
│    │              │           │     └─────── points ahead of (+) or behind (−) even pace
│    │              │           └───────────── used
│    │              └───────────────────────── pace: where even usage would put you now
│    └──────────────────────────────────────── usage
└───────────────────────────────────────────── window: 5h, 7d, opus, sonnet
```

<img src="docs/tray-icons.png" alt="Tray icon states" width="176" align="right">

| | |
|---|---|
| 🟢 **Green** | On or under pace |
| 🟠 **Orange** | Faster than pace |
| 🔴 **Red** | ≥ 90 % used, or > 15 points ahead of pace |

The **tray icon** shows the percentage and color of whichever window is most at risk. Hover it for a one-line summary of everything.

> **Tip:** a 5-hour window only starts when you send your first message, so early on you'll almost always be "ahead of pace". For 5h, watch the percentage; for 7d, the pace is what matters.

| Action | |
|---|---|
| Click the icon | Open / close the flyout |
| Right-click | Refresh · Sign in to Claude · Start with Windows · Quit |
| `F5` / `R`, or click the footer | Refresh now |
| `Esc`, or click elsewhere | Close |

The UI is in English, or Dutch when Windows is set to Dutch. Force a language with `USAGETRAY_LANG=en` or `nl`.

## Where the numbers come from

| | Source | Notes |
|---|---|---|
| **Codex** | Latest `token_count` event in `~/.codex/sessions/**/*.jsonl` | Local and read-only. Codex only logs limits while you use it, so older snapshots are labeled *as of 14:50*. If the window has reset since, it shows 0 %. |
| **Claude** | `api.anthropic.com/api/oauth/usage`, using the Claude Code CLI login in `~/.claude/.credentials.json` | Same data as `/usage` in Claude Code. Expired access tokens are refreshed and written back atomically, so the CLI keeps working. |

A provider that isn't installed is simply hidden.

### Privacy & security

- **Reads** your Codex session logs and the Claude Code credentials file.
- **Writes** only refreshed Claude tokens back to that same credentials file, plus the optional autostart entry (`HKCU\…\Run`).
- **Talks to** `platform.claude.com` (token refresh) and `api.anthropic.com` (usage). Nothing else. No telemetry, no analytics.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/MosrednA/UsageTray.git
cd UsageTray
dotnet run -c Release
```

| | |
|---|---|
| `UsageTray --snapshot out.png` | Render the flyout with your live data to a PNG |
| `UsageTray --render-assets` | Regenerate `docs/*.png` and `assets/app.ico` from demo data |

Tag a commit `v*` to build and publish a release automatically.

<details>
<summary>Project layout</summary>

| File | Role |
|---|---|
| `TrayApp.cs` | Tray icon, menu, refresh timer, Claude sign-in, autostart |
| `PopupForm.cs` | The flyout — custom-drawn, DPI-aware |
| `CodexSource.cs` | Codex session-log reader |
| `ClaudeSource.cs` | OAuth token refresh + usage endpoint |
| `Models.cs` | Usage windows, pace math, formatting |
| `IconRenderer.cs` | Tray icon and logo |
| `DocAssets.cs` | README images and `.ico` generator |
| `L.cs` | English / Dutch strings |

</details>

## Caveats

- The Claude usage endpoint is **undocumented** and may change without notice. If Claude stops showing up after an update, that's the likely cause.
- UsageTray reuses the Claude Code CLI login rather than logging in itself. The Claude desktop app keeps its own login, which UsageTray can't access.
- UsageTray is an independent project and is **not affiliated with or endorsed by OpenAI or Anthropic**. Codex and Claude are trademarks of their respective owners.

## License

[MIT](LICENSE)
