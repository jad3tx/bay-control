using BayControl.Host.Services;
using BayControl.Host.Services.Clips;
using BayControl.Host.Services.Launch;

namespace BayControl.Host.UI;

public sealed class DashboardForm : Form
{
    private readonly PairingService _pairing;
    private readonly AppState _state;
    private readonly SettingsService _settings;
    private readonly ProfileService _profiles;
    private readonly InputInjector _input;
    private readonly string _profilesDir;
    private readonly ShotIngestService _shots;
    private readonly LaunchMonitorCoordinator _launch;
    private readonly CameraService _cameras;
    private readonly ClipPipeline _clipPipeline;
    private readonly ClipStore _clipStore;
    private readonly NotifyIcon _tray;
    private readonly PictureBox _qr;
    private readonly Label _urlLabel;
    private readonly Label _pinLabel;
    private readonly Label _statusLabel;
    private readonly Label _healthLabel;
    private readonly Label _novaLabel;
    private readonly Label _lastShotLabel;
    private readonly Label _clipsLabel;
    private readonly ListBox _log;
    private readonly CheckBox _autostart;
    private readonly CheckBox _pauseRemote;
    private readonly CheckBox _novaEnabled;
    private readonly CheckBox _clipsEnabled;
    private readonly TextBox _novaHost;
    private readonly ComboBox _faceOnCam;
    private readonly ComboBox _dtlCam;
    private readonly ComboBox _clipRes;
    private readonly NumericUpDown _clipFps;
    private readonly NumericUpDown _portBox;
    private readonly ComboBox _testAction;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    public DashboardForm(
        PairingService pairing,
        AppState state,
        SettingsService settings,
        ProfileService profiles,
        InputInjector input,
        string profilesDir,
        ShotIngestService shots,
        LaunchMonitorCoordinator launch,
        CameraService cameras,
        ClipPipeline clipPipeline,
        ClipStore clipStore,
        Action requestExit)
    {
        _pairing = pairing;
        _state = state;
        _settings = settings;
        _profiles = profiles;
        _input = input;
        _profilesDir = profilesDir;
        _shots = shots;
        _launch = launch;
        _cameras = cameras;
        _clipPipeline = clipPipeline;
        _clipStore = clipStore;

        Text = "Bay Control";
        Width = 560;
        Height = 1018;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.FromArgb(18, 42, 28);
        ForeColor = Color.FromArgb(232, 240, 230);
        Font = new Font("Segoe UI", 10f);
        Icon = LoadAppIcon();

        var title = new Label
        {
            Text = "Bay Control",
            Font = new Font("Segoe UI Semibold", 20f),
            AutoSize = true,
            Location = new Point(20, 14)
        };

        var subtitle = new Label
        {
            Text = "Scan the QR · same Wi‑Fi · focus your sim",
            AutoSize = true,
            Location = new Point(22, 50),
            ForeColor = Color.FromArgb(180, 210, 185)
        };

        _qr = new PictureBox
        {
            Location = new Point(20, 82),
            Size = new Size(200, 200),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White
        };
        RefreshQr();

        _urlLabel = new Label
        {
            Text = pairing.GetControllerUrl(),
            Location = new Point(240, 82),
            Size = new Size(280, 56),
            ForeColor = Color.FromArgb(200, 230, 205)
        };

        _pinLabel = new Label
        {
            Text = $"PIN  {pairing.Pin}",
            Font = new Font("Segoe UI Semibold", 18f),
            Location = new Point(240, 145),
            AutoSize = true
        };

        _statusLabel = new Label
        {
            Text = "Waiting for clients…",
            Location = new Point(240, 185),
            Size = new Size(280, 40)
        };

        _healthLabel = new Label
        {
            Text = "Health: listening on LAN",
            Location = new Point(240, 230),
            Size = new Size(280, 40),
            ForeColor = Color.FromArgb(180, 210, 185)
        };

        _novaLabel = new Label
        {
            Text = "Nova: starting…",
            Location = new Point(240, 268),
            Size = new Size(280, 22),
            ForeColor = Color.FromArgb(240, 199, 94)
        };

        _lastShotLabel = new Label
        {
            Text = "Last shot: —",
            Location = new Point(20, 300),
            Size = new Size(500, 22),
            ForeColor = Color.FromArgb(180, 210, 185)
        };

        _autostart = new CheckBox
        {
            Text = "Start with Windows",
            Checked = AutostartService.IsEnabled(),
            Location = new Point(20, 330),
            AutoSize = true,
            ForeColor = Color.White
        };
        _autostart.CheckedChanged += (_, _) =>
        {
            AutostartService.SetEnabled(_autostart.Checked);
            _settings.Current.AutostartEnabled = _autostart.Checked;
            _settings.Save();
        };

        _pauseRemote = new CheckBox
        {
            Text = "Pause remote input",
            Location = new Point(200, 330),
            AutoSize = true,
            ForeColor = Color.White
        };
        _pauseRemote.CheckedChanged += (_, _) => state.SetRemotePaused(_pauseRemote.Checked);

        _novaEnabled = new CheckBox
        {
            Text = "Nova launch data",
            Checked = _settings.Current.NovaEnabled,
            Location = new Point(360, 330),
            AutoSize = true,
            ForeColor = Color.White
        };
        _novaEnabled.CheckedChanged += (_, _) =>
        {
            _settings.Current.NovaEnabled = _novaEnabled.Checked;
            _settings.Save();
            _launch.Stop();
            _launch.Start();
        };

        var portLabel = new Label { Text = "Port (next launch)", Location = new Point(20, 365), AutoSize = true };
        _portBox = new NumericUpDown
        {
            Minimum = 1024,
            Maximum = 65535,
            Value = pairing.Port,
            Location = new Point(150, 362),
            Width = 90
        };

        var savePortBtn = MakeButton("Save port", 250, 360, 100);
        savePortBtn.Click += (_, _) =>
        {
            _settings.Current.Port = (int)_portBox.Value;
            _settings.Save();
            MessageBox.Show(
                $"Port {_settings.Current.Port} saved. Restart Bay Control for it to take effect.",
                "Bay Control",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };

        var novaHostLabel = new Label { Text = "Nova host (blank = mDNS)", Location = new Point(20, 400), AutoSize = true };
        _novaHost = new TextBox
        {
            Text = _settings.Current.NovaHost ?? "",
            Location = new Point(200, 397),
            Width = 180
        };
        var saveNovaBtn = MakeButton("Save Nova", 390, 395, 100);
        saveNovaBtn.Click += (_, _) =>
        {
            _settings.Current.NovaHost = string.IsNullOrWhiteSpace(_novaHost.Text) ? null : _novaHost.Text.Trim();
            _settings.Save();
            _launch.Stop();
            _launch.Start();
            _state.LogAction("nova:settings_saved");
        };

        _clipsEnabled = new CheckBox
        {
            Text = "Swing clips",
            Checked = _settings.Current.ClipCaptureEnabled,
            Location = new Point(20, 430),
            AutoSize = true,
            ForeColor = Color.White
        };
        _clipsEnabled.CheckedChanged += (_, _) =>
        {
            _settings.Current.ClipCaptureEnabled = _clipsEnabled.Checked;
            _settings.Save();
            _cameras.ApplySettingsAndRestart();
        };

        _clipsLabel = new Label
        {
            Text = "Clips: —",
            Location = new Point(130, 432),
            Size = new Size(380, 22),
            ForeColor = Color.FromArgb(180, 210, 185)
        };

        var faceLabel = new Label { Text = "Face-on cam", Location = new Point(20, 462), AutoSize = true };
        _faceOnCam = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(120, 458),
            Width = 150
        };
        var dtlLabel = new Label { Text = "DTL cam", Location = new Point(280, 462), AutoSize = true };
        _dtlCam = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(350, 458),
            Width = 150
        };
        PopulateCameraCombos();

        var resLabel = new Label { Text = "Resolution", Location = new Point(20, 496), AutoSize = true };
        _clipRes = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(120, 492),
            Width = 150
        };
        PopulateResolutionCombo();

        var fpsLabel = new Label { Text = "FPS", Location = new Point(280, 496), AutoSize = true };
        _clipFps = new NumericUpDown
        {
            Minimum = CameraCaptureSession.MinFps,
            Maximum = CameraCaptureSession.MaxFps,
            Increment = 30,
            Value = Math.Clamp(_settings.Current.ClipFps, CameraCaptureSession.MinFps, CameraCaptureSession.MaxFps),
            Location = new Point(350, 492),
            Width = 80
        };

        var saveCamsBtn = MakeButton("Save cams", 20, 528, 100);
        saveCamsBtn.Click += (_, _) =>
        {
            _settings.Current.CameraFaceOnDeviceId = SelectedCameraId(_faceOnCam);
            _settings.Current.CameraDtlDeviceId = SelectedCameraId(_dtlCam);
            if (_clipRes.SelectedItem is ResItem res)
            {
                _settings.Current.ClipWidth = res.Width;
                _settings.Current.ClipHeight = res.Height;
            }
            _settings.Current.ClipFps = (int)_clipFps.Value;
            _settings.Save();
            _cameras.ApplySettingsAndRestart();
            _state.LogAction($"cams:saved({_settings.Current.ClipWidth}x{_settings.Current.ClipHeight}@{_settings.Current.ClipFps})");
        };
        var refreshCamsBtn = MakeButton("Refresh cams", 130, 528, 110);
        refreshCamsBtn.Click += (_, _) => PopulateCameraCombos();
        var testClipBtn = MakeButton("Test clip", 250, 528, 100);
        testClipBtn.Click += (_, _) => _clipPipeline.TestCapture();
        var openClipsBtn = MakeButton("Clips folder", 360, 528, 110);
        openClipsBtn.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = _clipStore.RootPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Bay Control");
            }
        };

        var row1Y = 572;
        var copyBtn = MakeButton("Copy URL", 20, row1Y, 100);
        copyBtn.Click += (_, _) => Clipboard.SetText(_pairing.GetControllerUrl());

        var openBtn = MakeButton("Open on this PC", 130, row1Y, 130);
        openBtn.Click += (_, _) => OpenUrl(_pairing.GetControllerUrl());

        var firewallBtn = MakeButton("Add firewall rule", 270, row1Y, 140);
        firewallBtn.Click += (_, _) =>
        {
            var ok = FirewallService.TryAddRule(_pairing.Port, out var msg);
            MessageBox.Show(msg, "Bay Control", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            _state.LogAction(ok ? "firewall:ok" : "firewall:fail");
        };

        var helpBtn = MakeButton("Help", 420, row1Y, 90);
        helpBtn.Click += (_, _) => OpenUrl("https://github.com/jad3tx/bay-control/blob/main/docs/install.md");

        var row2Y = 612;
        var profilesBtn = MakeButton("Open profiles folder", 20, row2Y, 160);
        profilesBtn.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = _profilesDir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Bay Control");
            }
        };

        var regenBtn = MakeButton("Regenerate PIN/QR", 190, row2Y, 150);
        regenBtn.Click += (_, _) =>
        {
            _pairing.RegenerateSecrets();
            _settings.Current.Pin = _pairing.Pin;
            _settings.Current.Token = _pairing.Token;
            _settings.Save();
            RefreshQr();
            _pinLabel.Text = $"PIN  {_pairing.Pin}";
            _urlLabel.Text = _pairing.GetControllerUrl();
            _state.LogAction("pairing:regenerated");
        };

        var bookmarkBtn = MakeButton("Bookmark tip", 350, row2Y, 120);
        bookmarkBtn.Click += (_, _) =>
            MessageBox.Show(_pairing.GetBookmarkHint(), "Add to Home Screen", MessageBoxButtons.OK,
                MessageBoxIcon.Information);

        var testLabel = new Label
        {
            Text = "Test action (sends keys now — focus Notepad or your sim first)",
            Location = new Point(20, 657),
            AutoSize = true,
            ForeColor = Color.FromArgb(180, 210, 185)
        };
        _testAction = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(20, 682),
            Width = 220
        };
        foreach (var id in _profiles.Profiles.Values.SelectMany(p => p.Actions.Keys).Distinct().OrderBy(x => x))
        {
            _testAction.Items.Add(id);
        }

        if (_testAction.Items.Count > 0)
        {
            _testAction.SelectedIndex = Math.Max(0, _testAction.Items.IndexOf("mulligan"));
            if (_testAction.SelectedIndex < 0)
            {
                _testAction.SelectedIndex = 0;
            }
        }

        var testBtn = MakeButton("Test", 250, 680, 80);
        testBtn.Click += (_, _) => RunTestAction();

        var simShotBtn = MakeButton("Simulate shot", 340, 680, 120);
        simShotBtn.Click += (_, _) =>
        {
            var shot = _shots.Ingest(ShotNormalizer.Simulate());
            _state.LogAction($"simulate:{shot.Id[..8]}");
        };

        _log = new ListBox
        {
            Location = new Point(20, 727),
            Size = new Size(500, 200),
            BackColor = Color.FromArgb(12, 28, 18),
            ForeColor = Color.FromArgb(200, 220, 200),
            BorderStyle = BorderStyle.FixedSingle
        };

        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(_qr);
        Controls.Add(_urlLabel);
        Controls.Add(_pinLabel);
        Controls.Add(_statusLabel);
        Controls.Add(_healthLabel);
        Controls.Add(_novaLabel);
        Controls.Add(_lastShotLabel);
        Controls.Add(_autostart);
        Controls.Add(_pauseRemote);
        Controls.Add(_novaEnabled);
        Controls.Add(portLabel);
        Controls.Add(_portBox);
        Controls.Add(savePortBtn);
        Controls.Add(novaHostLabel);
        Controls.Add(_novaHost);
        Controls.Add(saveNovaBtn);
        Controls.Add(_clipsEnabled);
        Controls.Add(_clipsLabel);
        Controls.Add(faceLabel);
        Controls.Add(_faceOnCam);
        Controls.Add(dtlLabel);
        Controls.Add(_dtlCam);
        Controls.Add(resLabel);
        Controls.Add(_clipRes);
        Controls.Add(fpsLabel);
        Controls.Add(_clipFps);
        Controls.Add(saveCamsBtn);
        Controls.Add(refreshCamsBtn);
        Controls.Add(testClipBtn);
        Controls.Add(openClipsBtn);
        Controls.Add(copyBtn);
        Controls.Add(openBtn);
        Controls.Add(firewallBtn);
        Controls.Add(helpBtn);
        Controls.Add(profilesBtn);
        Controls.Add(regenBtn);
        Controls.Add(bookmarkBtn);
        Controls.Add(testLabel);
        Controls.Add(_testAction);
        Controls.Add(testBtn);
        Controls.Add(simShotBtn);
        Controls.Add(_log);

        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "Bay Control",
            Icon = LoadAppIcon()
        };
        _tray.DoubleClick += (_, _) => ShowDashboard();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open dashboard", null, (_, _) => ShowDashboard());
        menu.Items.Add("Pause / resume", null, (_, _) => _pauseRemote.Checked = !_pauseRemote.Checked);
        menu.Items.Add("Add firewall rule", null, (_, _) => firewallBtn.PerformClick());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => requestExit());
        _tray.ContextMenuStrip = menu;

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += (_, _) => RefreshUi();
        _refreshTimer.Start();
        state.Changed += () =>
        {
            if (IsHandleCreated)
            {
                BeginInvoke(RefreshUi);
            }
        };
        pairing.Changed += () =>
        {
            if (IsHandleCreated)
            {
                BeginInvoke(() =>
                {
                    RefreshQr();
                    _pinLabel.Text = $"PIN  {_pairing.Pin}";
                    _urlLabel.Text = _pairing.GetControllerUrl();
                });
            }
        };

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                ShowInTaskbar = false;
            }
        };

        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
            }
        };

        RefreshUi();
    }

    public void ShowDashboard()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void RunTestAction()
    {
        if (_testAction.SelectedItem is not string actionId)
        {
            return;
        }

        var profile = _profiles.Profiles.Values.FirstOrDefault(p => p.Actions.ContainsKey(actionId))
                      ?? _profiles.Profiles.Values.FirstOrDefault();
        if (profile is null || !_profiles.TryGetAction(profile, actionId, out var mapping))
        {
            MessageBox.Show("Action not found in profiles.", "Bay Control");
            return;
        }

        try
        {
            _input.SendAction(mapping);
            _state.LogAction($"test:{actionId}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Bay Control");
        }
    }

    private void RefreshQr()
    {
        _qr.Image?.Dispose();
        using var ms = new MemoryStream(_pairing.CreateQrPng());
        _qr.Image = Image.FromStream(ms);
    }

    private void RefreshUi()
    {
        _statusLabel.Text = $"{(_state.RemotePaused ? "PAUSED · " : "")}{_state.ConnectedClients} client(s)\nPort {_pairing.Port}";
        _urlLabel.Text = _pairing.GetControllerUrl();
        _healthLabel.Text = BuildHealthText();

        var nova = _launch.NovaState;
        var tip = string.IsNullOrWhiteSpace(_launch.NovaDetail) ? "" : $" — {_launch.NovaDetail}";
        if (tip.Length > 48)
        {
            tip = tip[..45] + "…";
        }

        _novaLabel.Text = $"Nova: {nova}{tip}";
        _novaLabel.ForeColor = nova switch
        {
            "connected" => Color.FromArgb(125, 206, 160),
            "searching" or "connecting" => Color.FromArgb(240, 199, 94),
            "disabled" => Color.FromArgb(160, 170, 160),
            _ => Color.FromArgb(224, 122, 95)
        };

        var latest = _shots.Latest;
        if (latest is null)
        {
            _lastShotLabel.Text = "Last shot: waiting…";
        }
        else
        {
            var mph = ShotNormalizer.MpsToMph(latest.Ball.SpeedMps);
            _lastShotLabel.Text =
                $"Last shot: {mph:0.0} mph · VLA {latest.Ball.VlaDeg:0.0}° · spin {latest.Ball.TotalSpinRpm:0} · {latest.Source}";
        }

        _clipsLabel.Text = $"Clips: {_cameras.StatusSummary()} · {_clipStore.List(200).Count} saved";

        _log.Items.Clear();
        foreach (var line in _state.RecentActions.Reverse())
        {
            _log.Items.Add(line);
        }
    }

    private void PopulateCameraCombos()
    {
        var devices = _cameras.EnumerateDevices();
        FillCameraCombo(_faceOnCam, devices, _settings.Current.CameraFaceOnDeviceId);
        FillCameraCombo(_dtlCam, devices, _settings.Current.CameraDtlDeviceId);
    }

    private static void FillCameraCombo(ComboBox box, IReadOnlyList<VideoDeviceInfo> devices, string? selectedId)
    {
        box.Items.Clear();
        box.Items.Add(new CamItem(null, "(none)"));
        foreach (var d in devices)
        {
            box.Items.Add(new CamItem(d.Id, d.Name));
        }

        var idx = 0;
        for (var i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is CamItem item && item.Id == selectedId)
            {
                idx = i;
                break;
            }
        }

        box.SelectedIndex = idx;
    }

    private static string? SelectedCameraId(ComboBox box) =>
        box.SelectedItem is CamItem item ? item.Id : null;

    private sealed record CamItem(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    private void PopulateResolutionCombo()
    {
        var curW = _settings.Current.ClipWidth;
        var curH = _settings.Current.ClipHeight;
        var presets = new List<(int W, int H)> { (640, 480), (1280, 720), (1920, 1080) };
        if (curW > 0 && curH > 0 && !presets.Any(p => p.W == curW && p.H == curH))
        {
            presets.Add((curW, curH));
        }

        _clipRes.Items.Clear();
        var selected = 0;
        for (var i = 0; i < presets.Count; i++)
        {
            var (w, h) = presets[i];
            _clipRes.Items.Add(new ResItem(w, h));
            if (w == curW && h == curH)
            {
                selected = i;
            }
        }

        _clipRes.SelectedIndex = selected;
    }

    private sealed record ResItem(int Width, int Height)
    {
        public override string ToString() => $"{Width}x{Height}";
    }

    private string BuildHealthText()
    {
        var ip = _pairing.GetLanIp();
        if (ip is "127.0.0.1")
        {
            return "Health: no LAN IP — check Wi‑Fi on this PC";
        }

        if (_state.RemotePaused)
        {
            return "Health: remote paused from tray";
        }

        return $"Health: reachable at {ip}:{_pairing.Port} · same Wi‑Fi required";
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var beside = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(beside))
            {
                return new Icon(beside);
            }

            var exe = Application.ExecutablePath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                return Icon.ExtractAssociatedIcon(exe) ?? SystemIcons.Application;
            }
        }
        catch
        {
            // fall through
        }

        return SystemIcons.Application;
    }

    private static Button MakeButton(string text, int x, int y, int width) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(width, 32),
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(46, 110, 64),
        ForeColor = Color.White
    };

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _qr.Image?.Dispose();
        }

        base.Dispose(disposing);
    }
}
