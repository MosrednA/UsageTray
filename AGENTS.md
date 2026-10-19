# UsageTray

A small native Windows tray app (WinForms, .NET 10) that shows Codex and Claude subscription limits
with a pace marker, plus a memory view. It is public and generic: built for anyone who uses Codex
or Claude, not only this machine. [README.md](README.md) covers the data sources, privacy and the
build.

## Run and verify

- `dotnet run -c Release`. Check UI changes without clicking through the tray:
  `UsageTray --snapshot out.png` (live data), `--procs out.png` (memory view) and
  `--preview-icons out.png`.
- An agent's shell runs inside the Claude app's job object, so a tray started from it with
  `Start-Process` goes down when the Claude app restarts or updates. Restart the
  user's tray outside it: `Invoke-CimMethod Win32_Process -MethodName Create -Arguments
  @{CommandLine='"<path>\UsageTray.exe"'}`.
- `UsageTray --render-assets` regenerates `docs/*.png` and `assets/app.ico` from demo data. Use it
  for README images, never screenshots with real numbers.
- CI (`.github/workflows/ci.yml`) builds every push.

## Ship

- Push to `main` for source changes. A release is a `v*` tag, which the release workflow builds and
  publishes; bump the version first and ask the user before tagging.

## Notes

- Public repository with outside contributors: keep personal details, local paths and personal
  tools out of code, docs and commits. Personal additions (agent sessions, the bridge) belong in
  Agentwachter, which reuses this app's look so the two can merge later.
- The Claude usage endpoint, `~/.claude/sessions` and `codex app-server` are undocumented or
  experimental. Keep the fallbacks: the last snapshot, back-off on 429, and at most one Claude poll
  per five minutes.
- UI strings live in `L.cs` in English and Dutch; add both.
