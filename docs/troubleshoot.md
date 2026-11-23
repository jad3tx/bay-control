# Troubleshooting

## Phone cannot open the page / connection times out

- Confirm phone and PC are on the **same Wi‑Fi** (not Guest).
- On the PC dashboard, click **Add firewall rule**, then retry.
- Disable AP/client isolation on the router if devices cannot see each other.
- Try the URL shown on the dashboard in the phone browser (typed carefully).
- Temporarily pause VPN software on phone or PC.

## Wrong PIN / Unauthorized

- Use the **6-digit PIN** on the Bay Control window.
- Or scan the QR again (it includes a pairing token).
- Click **Regenerate PIN/QR** on the PC if you shared an old link.

## Status: “Paused — sim not focused”

- Click the GSPro or Pureplay window so it is in front.
- Fullscreen is fine; the window must still be the foreground app.
- Use the Profile dropdown → pick GSPro/Pureplay if auto-detect fails.

## Buttons do nothing while status looks connected

- Check **Pause remote input** is unchecked in the tray/dashboard.
- Confirm status is **Controlling: …**, not paused.
- Some Pureplay keys may differ — open **Profiles folder**, edit `pureplay.json`, restart, or use **Test action** on the PC.

## Trackpad / typing does nothing

- Same focus rules as buttons — sim must be foreground.
- Try **Left Click** once after moving; menus need a real click target.

## Controls stop after alt-tab / overlay

- Expected: focus guard blocks input when the sim is not active.
- Click back on the sim; the phone status should return to Controlling.

## Autostart / uninstall

- Uncheck **Start with Windows** in the dashboard, or remove the `BayControl` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- Delete the install folder and the Start Menu shortcut to remove the app.
- Firewall rule name: `Bay Control 8787` (or your configured port).

## Still stuck?

Open an issue at https://github.com/jad3tx/bay-control/issues with: Windows version, sim (GSPro/Pureplay), phone OS, and whether the dashboard QR loads on the PC browser via “Open on this PC.”
