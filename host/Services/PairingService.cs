using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using QRCoder;

namespace BayControl.Host.Services;

public sealed class PairingService
{
    public int Port { get; private set; }
    public string Pin { get; private set; }
    public string Token { get; private set; }

    public event Action? Changed;

    public PairingService(int port, string? pin = null, string? token = null)
    {
        Port = port > 0 ? port : 8787;
        Pin = string.IsNullOrWhiteSpace(pin)
            ? RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6")
            : pin.Trim();
        Token = string.IsNullOrWhiteSpace(token)
            ? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()
            : token.Trim().ToLowerInvariant();
    }

    public void SetPortForNextLaunch(int port) => Port = port;

    public void RegenerateSecrets()
    {
        Pin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        Changed?.Invoke();
    }

    public string GetLanIp()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var props = ni.GetIPProperties();
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address))
                    {
                        return addr.Address.ToString();
                    }
                }
            }
        }
        catch
        {
            // Fall through.
        }

        return "127.0.0.1";
    }

    public string GetControllerUrl() => $"http://{GetLanIp()}:{Port}/?t={Token}";

    public string GetBookmarkHint() =>
        "After connecting, use Share → Add to Home Screen. The QR link already includes your pairing token.";

    public bool ValidateToken(string? token) =>
        !string.IsNullOrWhiteSpace(token) &&
        string.Equals(token, Token, StringComparison.OrdinalIgnoreCase);

    public bool ValidatePin(string? pin) =>
        !string.IsNullOrWhiteSpace(pin) &&
        string.Equals(pin.Trim(), Pin, StringComparison.Ordinal);

    public byte[] CreateQrPng()
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(GetControllerUrl(), QRCodeGenerator.ECCLevel.Q);
        var qr = new PngByteQRCode(data);
        return qr.GetGraphic(8);
    }
}
