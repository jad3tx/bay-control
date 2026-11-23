using System.Diagnostics;

namespace BayControl.Host.Services;

public static class FirewallService
{
    public static bool TryAddRule(int port, out string message)
    {
        var name = $"Bay Control {port}";
        try
        {
            // Remove old rule with same name, then add.
            RunNetsh($"advfirewall firewall delete rule name=\"{name}\"", elevate: true);
            var code = RunNetsh(
                $"advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol=TCP localport={port} profile=private,domain",
                elevate: true);
            if (code == 0)
            {
                message = $"Firewall rule added for TCP {port} (private/domain).";
                return true;
            }

            message = "Firewall rule may have failed. Allow Bay Control when Windows prompts, or add the rule manually.";
            return false;
        }
        catch (Exception ex)
        {
            message = $"Could not add firewall rule: {ex.Message}";
            return false;
        }
    }

    private static int RunNetsh(string arguments, bool elevate)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            UseShellExecute = elevate,
            Verb = elevate ? "runas" : "",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        if (!elevate)
        {
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
        }

        using var p = Process.Start(psi);
        if (p is null)
        {
            return -1;
        }

        p.WaitForExit(15000);
        return p.ExitCode;
    }
}
