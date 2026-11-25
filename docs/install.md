# Install Bay Control

## Launch monitor (Nova)

Bay Control can show **last-shot metrics** from a Nova (Open Launch) on the same LAN:

1. Power on Nova and join the **same Wi‑Fi** as the sim PC (not Guest).
2. Leave **Nova launch data** checked on the Bay Control dashboard (default on).
3. Optional: set **Nova host** to the Nova IP if mDNS discovery fails; default WS port is **2920**.
4. Use **Simulate shot** on the dashboard to exercise the phone UI without hitting a ball.

Open Connect Tee (proxy on port 921) is reserved for a later build and stays off by default.

## Swing cameras (dual USB)

Bay Control can save **±3 second** clips around each shot from up to two USB webcams on the sim PC (face-on + down-the-line; club-face optional later):

1. Plug both cameras into the **sim PC** running Bay Control.
2. Allow camera access if Windows prompts (Settings → Privacy → Camera).
3. On the dashboard: enable **Swing clips**, pick **Face-on cam** and **DTL cam**, click **Save cams**.
4. Hit a ball (or **Simulate shot** / **Test clip**) — clips appear under **Swings** on the phone remote.
5. Files live in `%LocalAppData%\BayControl\clips\` (Open **Clips folder** on the dashboard).

## Update on the sim PC

Downloads the latest GitHub **release zip** (no Git or .NET SDK required):

```powershell
irm https://raw.githubusercontent.com/jad3tx/bay-control/main/tools/Update-BayControl.bootstrap.ps1 | iex
```

Optional: `.\tools\Update-BayControl.ps1 -Tag v1.3.0` or `-FromSource` (Git + .NET 8 SDK). App install path: `%LOCALAPPDATA%\BayControl\app`.

## Recommended (release zip)

1. Download **BayControl-win-x64.zip** from the [latest release](https://github.com/jad3tx/bay-control/releases/latest).
2. Extract the folder (example: `C:\BayControl`).
3. Run **`Install-BayControl.ps1`** (right-click → Run with PowerShell) *or* double-click **`BayControl.exe`**.
4. If Windows asks about Firewall, allow Bay Control on **private** networks (port **8787**). You can also click **Add firewall rule** in the dashboard.
5. Put your phone or tablet on the **same Wi‑Fi** as the sim PC.
6. Scan the QR code (or open the URL + enter the PIN).
7. Click into **GSPro** or **Pureplay** so they are focused, then use the remote.

### Add to Home Screen (optional)

- **iPhone/iPad:** Share → Add to Home Screen  
- **Android Chrome:** Menu → Install app / Add to Home screen  

## Network requirements

- Phone/tablet and sim PC must share the same LAN.
- Avoid **Guest Wi‑Fi** and router **AP / client isolation** — those block phone → PC traffic.
- Internet is not required for controls after the page is loaded (fonts may need internet once).

## What the installer script does

`Install-BayControl.ps1`:

- Creates a Start Menu shortcut
- Optionally enables Start with Windows
- Optionally adds a Windows Firewall inbound rule for TCP 8787
- Launches Bay Control

You can always run `BayControl.exe` alone without the script.

## First-run checklist

- [ ] Dashboard shows QR, URL, and 6-digit PIN  
- [ ] Phone opens the controller after scanning  
- [ ] Status says **Controlling: GSPro** or **Pureplay** when the sim is focused  
- [ ] A club or mulligan tap works from the mat  

If something fails, see [troubleshoot.md](troubleshoot.md).
