# Bay Control

Open-source local Wi‑Fi remote for **GSPro** and **Pureplay**. Run a small host on your Windows 11 sim PC, then open a web page on any phone, tablet, or PC on the same network.

No App Store install. Scan the QR code and play from the mat.

## Install (recommended)

1. Download **[BayControl-win-x64.zip](https://github.com/jad3tx/bay-control/releases/latest)** from the latest release.
2. Extract it, then run **`Install-BayControl.ps1`** (or just `BayControl.exe`).
3. Allow Firewall if prompted (or use **Add firewall rule** in the dashboard).
4. Phone/tablet on the **same Wi‑Fi** → scan the QR → focus GSPro/Pureplay.

Full walkthrough: [docs/install.md](docs/install.md) · Problems: [docs/troubleshoot.md](docs/troubleshoot.md)

## Features (v1)

- Labeled controls for club, aim, mulligan, heat map, flyover, shot cam, scorecard, and more
- Trackpad + on-screen typing for menus and player names
- GSPro + Pureplay profiles with auto-detect (or manual override)
- Focus safety: inputs only fire when the sim is the foreground window
- Multi-device: several phones/tablets can connect to one bay
- Autostart with Windows, tray dashboard, PIN + QR pairing, firewall helper
- First-run setup tips and action test from the PC
- Golf-themed responsive UI (phone & tablet, portrait & landscape)

## Architecture

```
Phone / tablet browser  --HTTP-->  Bay Control Host (serves UI)
Phone / tablet browser  <--WebSocket-->  Host (actions / trackpad)
Host  --SendInput-->  GSPro or Pureplay (when focused)
```

Everything stays on your home LAN. GitHub is only for source and releases.

## Configure

From the Bay Control dashboard / tray:

| Control | What it does |
|---------|----------------|
| Start with Windows | Autostart (on by default; uncheck to disable) |
| Pause remote input | Blocks all phone/tablet input |
| Add firewall rule | Elevates once to allow TCP port inbound |
| Open profiles folder | Edit `gspro.json` / `pureplay.json` |
| Regenerate PIN/QR | New pairing secrets (phones must reconnect) |
| Port | Saved for next launch (restart required) |
| Test action | Fires a shortcut on the PC to verify mappings |
| Open on this PC | Opens the controller in your local browser |

Profiles: [`profiles/`](profiles/). Keep action IDs stable when editing keys.

## Develop

Requirements: Windows 11, [.NET 8 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet run --project host
dotnet test
dotnet publish host/BayControl.Host.csproj -c Release -r win-x64 --self-contained true -o artifacts/BayControl
```

Cut a GitHub release (commits if needed, tags from the csproj `<Version>`, pushes so CI builds the zip):

```powershell
.\tools\Publish-Release.ps1 -Message "Play-first layout: Play / More / Trackpad."
.\tools\Publish-Release.ps1 -DryRun   # preview only
```

Redeploy on the **sim PC** (needs Git + .NET 8 SDK). Clones/pulls, publishes, replaces the install, restarts:

```powershell
# On the sim PC — downloads the latest release zip (no Git / no SDK needed):
irm https://raw.githubusercontent.com/jad3tx/bay-control/main/tools/Update-BayControl.bootstrap.ps1 | iex

# Optional: pin a tag, or build from source (needs Git + .NET 8 SDK):
#   ... | iex   then edit, or download Update-BayControl.ps1 and run:
.\tools\Update-BayControl.ps1 -Tag v1.3.0
.\tools\Update-BayControl.ps1 -FromSource
```

Install lands in `%LOCALAPPDATA%\BayControl\app`.

### Layout

| Path | Purpose |
|------|---------|
| `host/` | Windows tray host, Kestrel, WebSocket, input, focus guard |
| `web/` | Controller UI (static HTML/CSS/JS) |
| `profiles/` | Sim action → key maps |
| `docs/` | Install, troubleshoot, contributing |
| `tests/` | Unit tests |
| `Install-BayControl.ps1` | Optional setup helper shipped in the release zip |
| `tools/Publish-Release.ps1` | Tag + push a versioned GitHub Release |
| `tools/Update-BayControl.ps1` | Clone/pull + publish + reinstall on the sim PC |

## Security notes

- LAN only; no cloud relay
- PIN required (QR embeds a pairing token)
- Focus guard drops input when the sim is not focused
- You can Pause remote input from the tray

## Backlog

- Live course-map / screen streaming
- User-editable layouts
- Macros / smart menu clicks
- Additional simulators

## License

MIT — see [LICENSE](LICENSE).

GSPro and Pureplay are trademarks of their respective owners. Bay Control is an independent open-source project and is not affiliated with them.
