using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using QRCoder;

namespace BayControl.Host.Services;

public sealed class PairingService
{
    public int Port { get; }
    public string Pin { get; }
    public string Token { get; }

    public PairingService(int port = 8787)
    {
        Port = port;
        Pin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
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

    public string GetControllerUrl()
    {
        return $"http://{GetLanIp()}:{Port}/?t={Token}";
    }

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
