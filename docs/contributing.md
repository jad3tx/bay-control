# Contributing

Bay Control is MIT-licensed. PRs welcome.

## Dev loop

```powershell
dotnet run --project host
# open the QR URL on a phone on the same Wi‑Fi
dotnet test
```

End-user install docs live in [install.md](install.md) and [troubleshoot.md](troubleshoot.md). Keep those accurate when you change pairing, ports, or firewall behavior.

## Profiles

Edit `profiles/gspro.json` or `profiles/pureplay.json` and restart the host. Keep action IDs stable so the fixed web layout (`web/app.js` `LAYOUT`) keeps muscle memory. Disable missing actions in the UI by omitting them from a profile.

Use **Test action** on the PC dashboard to verify a mapping without rebuilding.

## Layout extensibility

v1 uses a fixed `LAYOUT` map in `web/app.js`. Future dynamic layouts should keep the same action IDs and stream layout JSON from the host without changing the WebSocket command protocol (`action`, `mouse_*`, `type`, `set_profile`).

## Settings

User settings persist in `%AppData%\BayControl\settings.json` (port, PIN/token, first-run flag, autostart preference).
