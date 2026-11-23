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
        if (hwnd == IntPtr.Zero)
        {
            return FocusState.Paused(null, null, null);
        }

        _ = GetWindowThreadProcessId(hwnd, out var pid);
        string? processName = null;
        try
        {
            processName = Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            // Process may have exited between queries.
        }

        var title = GetWindowTitle(hwnd);
        var profile = profiles.ResolveActive(processName, title);
        if (profile is null)
        {
            return FocusState.Paused(processName, title, null);
        }

        return FocusState.Active(processName, title, profile);
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
