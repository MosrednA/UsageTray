# UsageTray

**Je Codex- en Claude-limieten in één oogopslag, vanuit het Windows-systeemvak.**

Een klein, native tray-programma (C# / WinForms, geen dependencies) dat laat zien hoeveel je van je abonnementslimieten hebt verbruikt — en vooral: **of je op schema ligt** om de reset te halen.

<p align="center">
  <img src="docs/flyout.png" alt="UsageTray flyout met Codex- en Claude-limieten" width="420">
</p>

---

## Zo lees je het

Elke regel is één rollend limietvenster:

```
7d   ███████████████▏░░░░░░░░   60%   +7   3d 7h
│    │              │           │     │    └─ tijd tot reset
│    │              │           │     └────── verschil met pace (procentpunten)
│    │              │           └──────────── verbruikt
│    │              └──────────────────────── pace: waar je bij gelijkmatig verbruik zou zitten
│    └─────────────────────────────────────── verbruik
└──────────────────────────────────────────── venster (5h, 7d, opus, sonnet)
```

**Voorbij de witte streep = je gaat te hard.** `+7` betekent 7 procentpunten meer verbruikt dan gelijkmatig verdeeld; `−8` betekent ruimte over.

| Kleur | Betekenis |
|---|---|
| 🟢 Groen | Op of onder pace |
| 🟠 Oranje | Sneller dan pace |
| 🔴 Rood | ≥ 90% verbruikt, of > 15 punten voor op pace |

Het **tray-icoon** toont het percentage (en de kleur) van het venster met het meeste risico. Hover voor een samenvatting van alles.

## Bediening

| Actie | Effect |
|---|---|
| Klik op icoon | Flyout openen / sluiten |
| Rechtsklik | Vernieuwen · Start met Windows · Afsluiten |
| `F5` / `R` of klik op footer | Direct verversen |
| `Esc` / klik ernaast | Flyout sluiten |

Data ververst automatisch elke 2 minuten, en bij het openen van de flyout als die ouder is dan 30 seconden.

## Installatie

Vereist de [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/MosrednA/UsageTray.git
cd UsageTray
dotnet publish -c Release -o publish
```

Start `publish\UsageTray.exe`, en vink via rechtsklik **Start met Windows** aan.

**Icoon altijd zichtbaar maken:** sleep het vanuit het `^`-overloopmenu naar de taakbalk, of zet het aan via *Taakbalkinstellingen → Andere systeemvakpictogrammen → UsageTray*.

## Databronnen

### Codex — lokaal, geen login

Codex schrijft bij elke turn een `token_count`-event met de actuele rate limits in zijn sessielogs (`~/.codex/sessions/YYYY/MM/DD/*.jsonl`). UsageTray leest het nieuwste event uit de recentste sessies (alleen de staart van elk bestand, met cache per bestand).

- Toont plan, venster(s), resettijd en creditsaldo.
- Wordt alleen bijgewerkt wanneer je Codex gebruikt; oudere snapshots krijgen een label als `5h 06m oud`.
- Is het venster sindsdien gereset, dan toont hij 0%.

### Claude — via de Claude Code CLI-login

Gebruikt dezelfde OAuth-credentials (`~/.claude/.credentials.json`) en hetzelfde endpoint als `/usage` in Claude Code (`api.anthropic.com/api/oauth/usage`).

- Toont het 5-uurs- en weekvenster, plus model-specifieke weekvensters (Opus/Sonnet) zodra die verbruik hebben.
- Verlopen access tokens worden ververst en (atomisch) teruggeschreven, zodat de CLI gewoon blijft werken.
- Ziet de flyout `login verlopen`? Draai eenmalig:

  ```bash
  claude auth login
  ```

> ⚠️ Het Claude-endpoint is **niet officieel gedocumenteerd** en kan zonder aankondiging veranderen. De Claude-desktopapp gebruikt zijn eigen login; daar kan UsageTray niet bij — vandaar de CLI-login.

## Projectstructuur

| Bestand | Rol |
|---|---|
| `Program.cs` | Entry point, single-instance, `--snapshot` |
| `TrayApp.cs` | Tray-icoon, menu, refresh-timer, autostart |
| `PopupForm.cs` | De flyout (custom getekend, DPI-aware) |
| `IconRenderer.cs` | Dynamisch tray-icoon |
| `CodexSource.cs` | Codex-sessielogs uitlezen |
| `ClaudeSource.cs` | OAuth-token + usage-endpoint |
| `Models.cs` | Vensters, pace-berekening, formattering |
| `Theme.cs` | Kleuren en vormen |

### Layout tweaken

```bash
dotnet run -c Release -- --snapshot out.png
```

Rendert de flyout met live data naar een PNG en sluit af — handig om de layout te checken zonder het systeemvak te gebruiken.
