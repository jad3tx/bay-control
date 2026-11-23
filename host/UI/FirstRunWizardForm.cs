using BayControl.Host.Services;

namespace BayControl.Host.UI;

public sealed class FirstRunWizardForm : Form
{
    public FirstRunWizardForm(PairingService pairing, SettingsService settings)
    {
        Text = "Bay Control — First run";
        Width = 520;
        Height = 420;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(18, 42, 28);
        ForeColor = Color.FromArgb(232, 240, 230);
        Font = new Font("Segoe UI", 10f);

        var title = new Label
        {
            Text = "Welcome to Bay Control",
            Font = new Font("Segoe UI Semibold", 18f),
            AutoSize = true,
            Location = new Point(24, 20)
        };

        var body = new Label
        {
            Location = new Point(24, 60),
            Size = new Size(460, 220),
            Text =
                "1. Allow Windows Firewall for private networks (port " + pairing.Port + ").\n" +
                "   Use “Add firewall rule” on the next screen if no prompt appears.\n\n" +
                "2. Put your phone/tablet on the SAME Wi‑Fi as this PC.\n" +
                "   Avoid Guest Wi‑Fi and AP/client isolation.\n\n" +
                "3. Scan the QR code, or open the URL and enter the PIN.\n\n" +
                "4. Click into GSPro or Pureplay so they are focused — then play.\n\n" +
                "Tip: On your phone, Add to Home Screen for a full-screen remote."
        };

        var autostart = new CheckBox
        {
            Text = "Start Bay Control with Windows",
            Checked = settings.Current.AutostartEnabled,
            Location = new Point(24, 290),
            AutoSize = true,
            ForeColor = Color.White
        };

        var next = new Button
        {
            Text = "Continue",
            Location = new Point(360, 330),
            Size = new Size(120, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(46, 110, 64),
            ForeColor = Color.White
        };
        next.Click += (_, _) =>
        {
            settings.Current.AutostartEnabled = autostart.Checked;
            AutostartService.SetEnabled(autostart.Checked);
            settings.Current.FirstRunCompleted = true;
            settings.Save();
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(title);
        Controls.Add(body);
        Controls.Add(autostart);
        Controls.Add(next);
        AcceptButton = next;
    }
}
