# UsageTray

A small native Windows tray app (WinForms, .NET 10) that shows Codex and Claude subscription limits
with a pace marker, plus a memory view. It is public and generic: built for anyone who uses Codex
or Claude, not only this machine. [README.md](README.md) covers the data sources, privacy and the
build.

## Run and verify

- `dotnet run -c Release`. Check UI changes without clicking through the tray:
  `UsageTray --snapshot out.png` (live data), `--procs out.png` (memory view),
  `--sessions Claude out.png` (sessions panel), `--preview-icons out.png` and `--claude-login` (the
  sign-in window; set `CLAUDE_CONFIG_DIR` to a temp folder and `BROWSER` to a no-op exe to test it
  without touching the real login).
- An agent's shell runs inside the Claude app's job object, so a tray started from it with
  `Start-Process` goes down when the Claude app restarts or updates. Restart the
  user's tray outside it: `Invoke-CimMethod Win32_Process -MethodName Create -Arguments
  @{CommandLine='"<path>\UsageTray.exe"'}`.
- `UsageTray --render-assets` regenerates `docs/*.png` and `assets/app.ico` from demo data. Use it
  for README images, never screenshots with real numbers. The result depends on the display scale
  and your quick settings and contains the clock, so commit a re-render only when its content changes.
- CI (`.github/workflows/ci.yml`) builds every push.

## Ship

- Push to `main` for source changes. A release is a `v*` tag, which the release workflow builds and
  publishes; bump the version first and ask the user before tagging.

## Notes

- Public repository with outside contributors: keep personal details, local paths and personal
  tools out of code, docs and commits. Personal additions (such as the bridge) belong in
  Agentwachter, which reuses this app's look so the two can merge later.
- The Claude usage endpoint, `~/.claude/sessions`, the desktop app's session records and
  `codex app-server` are undocumented or experimental. Keep the fallbacks: the last snapshot, back-off
  on 429, at most one Claude poll per five minutes, and a flat session list when no parent is known.
- CLI sessions have no parent field. Nesting there comes from process ancestry, so only a session
  started from inside another one nests; the desktop app's link is `spawnedFrom.sessionId`.
- `claude auth login` puts a console in raw mode (no echo, no console Ctrl+V), so a pasted code looks
  lost and a stray Ctrl+V reports "Invalid code". That is why `ClaudeLoginForm` runs it with pipes:
  it writes the code to stdin as one line and needs only the exit code and stderr.
- UI strings live in `L.cs` in English and Dutch; add both.
