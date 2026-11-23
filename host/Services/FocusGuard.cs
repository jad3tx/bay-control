using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BayControl.Host.Models;

namespace BayControl.Host.Services;

public sealed class FocusGuard(ProfileService profiles)
{
    public FocusState GetState()
    {
        var hwnd = GetForegroundWindow();
        string? processName = null;
        string? title = null;

        if (hwnd != IntPtr.Zero)
        {
            _ = GetWindowThreadProcessId(hwnd, out var pid);
            try
            {
                processName = Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
                // Process may have exited between queries.
            }

            title = GetWindowTitle(hwnd);
            var foregroundProfile = profiles.ResolveActive(processName, title);
            if (foregroundProfile is not null)
            {
                return FocusState.Active(processName, title, foregroundProfile);
            }
        }

        // Same-PC browser testing: Bay Control UI steals focus on tap, but the sim
        // (or an RDP window titled like GSPro) is still running. Report as active so
        // the phone UI stays armed; injection will refocus before SendInput.
        foreach (var profile in profiles.Profiles.Values)
        {
            if (WindowFocus.TryFindRunning(profile, out _, out var detail))
            {
                return FocusState.Active(processName, title ?? detail, profile);
            }
        }

        return FocusState.Paused(processName, title, null);
    }

    public bool AllowInput(out SimProfile? profile, out string reason)
    {
        var state = GetState();
        profile = state.Profile;
        if (!state.IsSimFocused || profile is null)
        {
            reason = "Paused — sim not focused";
            return false;
        }

        reason = $"Controlling: {profile.DisplayName}";
        return true;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        _ = GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
}

public sealed record FocusState(
    bool IsSimFocused,
    string? ProcessName,
    string? WindowTitle,
    SimProfile? Profile,
    string StatusText)
{
    public static FocusState Active(string? process, string? title, SimProfile profile) =>
        new(true, process, title, profile, $"Controlling: {profile.DisplayName}");

    public static FocusState Paused(string? process, string? title, SimProfile? profile) =>
        new(false, process, title, profile, "Paused — sim not focused");
}
