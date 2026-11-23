# Bay Control

Open-source local Wi‑Fi remote for **GSPro** and **Pureplay**. Run a small host on your Windows 11 sim PC, then open a web page on any phone, tablet, or PC on the same network.

No App Store install. Scan the QR code and play from the mat.

## Features (v1)

- Labeled controls for club, aim, mulligan, heat map, flyover, shot cam, scorecard, and more
- Trackpad + on-screen typing for menus and player names
- GSPro + Pureplay profiles with auto-detect (or manual override)
- Focus safety: inputs only fire when the sim is the foreground window
- Multi-device: several phones/tablets can connect to one bay
- Autostart with Windows, system tray dashboard, PIN + QR pairing
- Golf-themed responsive UI (phone & tablet, portrait & landscape)

## Architecture

```
Phone / tablet browser  --HTTP-->  Bay Control Host (serves UI)
Phone / tablet browser  <--WebSocket-->  Host (actions / trackpad)
Host  --SendInput-->  GSPro or Pureplay (when focused)
```

Everything stays on your home LAN. GitHub is only for source and releases.

## Quick start

### Requirements

- Windows 11 sim PC
- .NET 8 runtime (or use the published self-contained build)
- Phone/tablet on the **same Wi‑Fi** as the sim PC

### Run from source

```powershell
cd host
dotnet run
```

1. Allow the Windows Firewall prompt for port **8787** if asked.
2. Scan the QR code on the Bay Control window (or open the URL shown).
3. Enter the 6-digit PIN if the QR token is not in the link.
4. Focus GSPro or Pureplay and use the controller.

### Publish a portable build

```powershell
dotnet publish host/BayControl.Host.csproj -c Release -r win-x64 --self-contained true -o artifacts/BayControl
```

Zip `artifacts/BayControl` and run `BayControl.exe` on the sim PC.

## Profiles

JSON profiles live in [`profiles/`](profiles/):

- `gspro.json` — GSPro keyboard mappings
- `pureplay.json` — Pureplay mappings (seeded to match common GSPro-like shortcuts; edit as needed)

Edit keys without rebuilding. Restart the host after changes. Action IDs are shared so the UI layout stays stable across sims.

## Development

```powershell
dotnet build
dotnet test
```

### Layout

| Path | Purpose |
|------|---------|
| `host/` | Windows tray host, Kestrel, WebSocket, input, focus guard |
| `web/` | Controller UI (static HTML/CSS/JS PWA; no build step) |
| `profiles/` | Sim action → key maps |
| `docs/` | Extra docs |
| `tests/` | Unit tests |

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
