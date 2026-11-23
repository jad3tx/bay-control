using BayControl.Host.Services;

namespace BayControl.Host.UI;

public sealed class DashboardForm : Form
{
    private readonly PairingService _pairing;
    private readonly AppState _state;
    private readonly NotifyIcon _tray;
    private readonly PictureBox _qr;
    private readonly Label _urlLabel;
    private readonly Label _pinLabel;
    private readonly Label _statusLabel;
    private readonly ListBox _log;
    private readonly CheckBox _autostart;
    private readonly CheckBox _pauseRemote;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    public DashboardForm(PairingService pairing, AppState state, Action requestExit)
    {
        _pairing = pairing;
        _state = state;

        Text = "Bay Control";
        Width = 520;
        Height = 640;
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
            Location = new Point(20, 16)
        };

        var subtitle = new Label
        {
            Text = "Scan the QR code on your phone or tablet",
            AutoSize = true,
            Location = new Point(22, 54),
            ForeColor = Color.FromArgb(180, 210, 185)
        };

        _qr = new PictureBox
        {
            Location = new Point(20, 90),
            Size = new Size(220, 220),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White
        };

        using var ms = new MemoryStream(pairing.CreateQrPng());
        _qr.Image = Image.FromStream(ms);

        _urlLabel = new Label
        {
            Text = pairing.GetControllerUrl(),
            Location = new Point(260, 90),
            Size = new Size(220, 70),
            ForeColor = Color.FromArgb(200, 230, 205)
        };

        _pinLabel = new Label
        {
            Text = $"PIN  {pairing.Pin}",
            Font = new Font("Segoe UI Semibold", 18f),
            Location = new Point(260, 170),
            AutoSize = true
        };

        _statusLabel = new Label
        {
            Text = "Waiting for clients…",
            Location = new Point(260, 220),
            Size = new Size(220, 60)
        };

        _autostart = new CheckBox
        {
            Text = "Start with Windows",
            Checked = AutostartService.IsEnabled(),
            Location = new Point(260, 280),
            AutoSize = true,
            ForeColor = Color.White
        };
        _autostart.CheckedChanged += (_, _) => AutostartService.SetEnabled(_autostart.Checked);

        _pauseRemote = new CheckBox
        {
            Text = "Pause remote input",
            Location = new Point(260, 310),
            AutoSize = true,
            ForeColor = Color.White
        };
        _pauseRemote.CheckedChanged += (_, _) => state.SetRemotePaused(_pauseRemote.Checked);

        var copyBtn = new Button
        {
            Text = "Copy URL",
            Location = new Point(260, 350),
            Size = new Size(100, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(46, 110, 64),
            ForeColor = Color.White
        };
        copyBtn.Click += (_, _) => Clipboard.SetText(pairing.GetControllerUrl());

        var openBtn = new Button
        {
            Text = "Open",
            Location = new Point(370, 350),
            Size = new Size(80, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(46, 110, 64),
            ForeColor = Color.White
        };
        openBtn.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pairing.GetControllerUrl(),
                    UseShellExecute = true
                });
            }
            catch
            {
                // ignore
            }
        };

        _log = new ListBox
        {
            Location = new Point(20, 330),
            Size = new Size(460, 240),
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
        Controls.Add(_autostart);
        Controls.Add(_pauseRemote);
        Controls.Add(copyBtn);
        Controls.Add(openBtn);
        Controls.Add(_log);

        // Reposition log below buttons
        _log.Location = new Point(20, 400);
        _log.Size = new Size(460, 180);

        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "Bay Control",
            Icon = SystemIcons.Application
        };
        _tray.DoubleClick += (_, _) => ShowDashboard();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open dashboard", null, (_, _) => ShowDashboard());
        menu.Items.Add("Pause / resume", null, (_, _) =>
        {
            _pauseRemote.Checked = !_pauseRemote.Checked;
        });
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

    private void RefreshUi()
    {
        _statusLabel.Text = stateText();
        _urlLabel.Text = _pairing.GetControllerUrl();
        _log.Items.Clear();
        foreach (var line in _state.RecentActions.Reverse())
        {
            _log.Items.Add(line);
        }

        string stateText()
        {
            var pause = _state.RemotePaused ? "PAUSED · " : "";
            return $"{pause}{_state.ConnectedClients} client(s) connected\nPort {_pairing.Port}";
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
