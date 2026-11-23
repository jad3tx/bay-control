using BayControl.Host.Services;

namespace BayControl.Host.UI;

public sealed class DashboardForm : Form
{
    private readonly PairingService _pairing;
    private readonly AppState _state;
    private readonly SettingsService _settings;
    private readonly ProfileService _profiles;
    private readonly InputInjector _input;
    private readonly string _profilesDir;
    private readonly NotifyIcon _tray;
    private readonly PictureBox _qr;
    private readonly Label _urlLabel;
    private readonly Label _pinLabel;
    private readonly Label _statusLabel;
    private readonly Label _healthLabel;
    private readonly ListBox _log;
    private readonly CheckBox _autostart;
    private readonly CheckBox _pauseRemote;
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
        Action requestExit)
    {
        _pairing = pairing;
        _state = state;
        _settings = settings;
        _profiles = profiles;
        _input = input;
        _profilesDir = profilesDir;

        Text = "Bay Control";
        Width = 560;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.FromArgb(18, 42, 28);
        ForeColor = Color.FromArgb(232, 240, 230);
        Font = new Font("Segoe UI", 10f);

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

        _autostart = new CheckBox
        {
            Text = "Start with Windows",
            Checked = AutostartService.IsEnabled(),
            Location = new Point(20, 300),
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
            Location = new Point(200, 300),
            AutoSize = true,
            ForeColor = Color.White
        };
        _pauseRemote.CheckedChanged += (_, _) => state.SetRemotePaused(_pauseRemote.Checked);

        var portLabel = new Label { Text = "Port (next launch)", Location = new Point(20, 335), AutoSize = true };
        _portBox = new NumericUpDown
        {
            Minimum = 1024,
            Maximum = 65535,
            Value = pairing.Port,
            Location = new Point(150, 332),
            Width = 90
        };

        var savePortBtn = MakeButton("Save port", 250, 330, 100);
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

        var row1Y = 375;
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

        var row2Y = 415;
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
            Location = new Point(20, 460),
            AutoSize = true,
            ForeColor = Color.FromArgb(180, 210, 185)
        };
        _testAction = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(20, 485),
            Width = 280
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

        var testBtn = MakeButton("Test", 310, 483, 90);
        testBtn.Click += (_, _) => RunTestAction();

        _log = new ListBox
        {
            Location = new Point(20, 530),
            Size = new Size(500, 160),
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
        Controls.Add(_autostart);
        Controls.Add(_pauseRemote);
        Controls.Add(portLabel);
        Controls.Add(_portBox);
        Controls.Add(savePortBtn);
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
        Controls.Add(_log);

        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "Bay Control",
            Icon = SystemIcons.Application
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
        _log.Items.Clear();
        foreach (var line in _state.RecentActions.Reverse())
        {
            _log.Items.Add(line);
        }
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
