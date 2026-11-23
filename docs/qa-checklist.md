# QA acceptance checklist (v1)

Use on a Windows 11 sim PC with a phone/tablet on the same home Wi‑Fi.

## Install / launch

- [ ] `dotnet run --project host` or published `BayControl.exe` starts
- [ ] Tray icon appears; dashboard shows QR, URL, and 6-digit PIN
- [ ] Autostart is enabled (Start with Windows checked)
- [ ] Windows Firewall allows inbound TCP 8787 when prompted

## Pairing

- [ ] Scanning QR opens the controller in mobile Safari/Chrome
- [ ] Opening URL without token prompts for PIN; wrong PIN rejected
- [ ] Correct PIN unlocks controls; reconnect survives refresh

## GSPro

- [ ] With GSPro focused, status shows `Controlling: GSPro`
- [ ] Club up/down, aim pad, mulligan, heat map, flyover, shot cam, scorecard work
- [ ] Alt-tab away → status `Paused — sim not focused` and taps do nothing
- [ ] Fullscreen GSPro still receives shortcuts

## Pureplay

- [ ] Focusing Pureplay switches status / profile (or manual override works)
- [ ] Shared actions still fire; missing actions appear disabled

## Multi-client / devices

- [ ] Two phones connected; both can press buttons without host crash
- [ ] Tablet landscape and phone portrait are usable
- [ ] Trackpad moves cursor; left/right click and scroll work when sim focused
- [ ] Type box sends characters into the focused sim

## Resilience

- [ ] Airplane mode / Wi‑Fi drop → Offline banner; restore auto-reconnects
- [ ] Tray “Pause remote input” blocks all injection until cleared
