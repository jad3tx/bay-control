using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BayControl.Host.Models;

namespace BayControl.Host.Services;

/// <summary>
/// Finds the sim window and forces it foreground before injecting keys.
/// Needed when the Bay Control browser on the same PC steals focus on click.
/// </summary>
public static class WindowFocus
{
    public static bool TryFocusSim(ProfileService profiles, out IntPtr hwnd, out string detail)
    {
        hwnd = IntPtr.Zero;
        detail = "sim window not found";

        foreach (var profile in profiles.Profiles.Values)
        {
            if (TryFindRunning(profile, out hwnd, out detail))
            {
                ForceForeground(hwnd);
                Thread.Sleep(40);
                return true;
            }
        }

        return false;
    }

    public static bool TryFocusProfile(SimProfile profile, out IntPtr hwnd, out string detail)
    {
        if (!TryFindRunning(profile, out hwnd, out detail))
        {
            return false;
        }

        ForceForeground(hwnd);
        Thread.Sleep(40);
        return true;
    }

    public static bool TryFindRunning(SimProfile profile, out IntPtr hwnd, out string detail)
    {
        hwnd = IntPtr.Zero;
        detail = $"no window for {profile.Id}";

        // Prefer matching process main window.
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                var name = process.ProcessName;
                if (profile.ProcessNames.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    hwnd = process.MainWindowHandle;
                    detail = $"{profile.DisplayName} via process {name}";
                    return true;
                }

                var title = process.MainWindowTitle;
                if (!string.IsNullOrWhiteSpace(title) &&
                    profile.WindowTitleContains.Any(t => title.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    hwnd = process.MainWindowHandle;
                    detail = $"{profile.DisplayName} via title '{title}'";
                    return true;
                }
            }
            catch
            {
                // process exited
            }
            finally
            {
                process.Dispose();
            }
        }

        // Enumerate all top-level windows as fallback (covers some RDP title cases).
        IntPtr found = IntPtr.Zero;
        string foundDetail = detail;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h))
            {
                return true;
            }

            var sb = new StringBuilder(512);
            _ = GetWindowText(h, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            if (profile.WindowTitleContains.Any(t => title.Contains(t, StringComparison.OrdinalIgnoreCase)))
            {
                found = h;
                foundDetail = $"{profile.DisplayName} via enum '{title}'";
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (found != IntPtr.Zero)
        {
            hwnd = found;
            detail = foundDetail;
            return true;
        }

        return false;
    }

    private static void ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var foreground = GetForegroundWindow();
        if (foreground == hwnd)
        {
            return;
        }

        var targetThread = GetWindowThreadProcessId(hwnd, out _);
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();

        try
        {
            if (foregroundThread != currentThread)
            {
                _ = AttachThreadInput(currentThread, foregroundThread, true);
            }

            if (targetThread != currentThread)
            {
                _ = AttachThreadInput(currentThread, targetThread, true);
            }

            _ = ShowWindow(hwnd, 9); // SW_RESTORE
            _ = SetForegroundWindow(hwnd);
            _ = BringWindowToTop(hwnd);
        }
        finally
        {
            if (targetThread != currentThread)
            {
                _ = AttachThreadInput(currentThread, targetThread, false);
            }

            if (foregroundThread != currentThread)
            {
                _ = AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
}
