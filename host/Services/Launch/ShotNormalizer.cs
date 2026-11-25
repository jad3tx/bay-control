using System.Text.Json;
using BayControl.Host.Models;

namespace BayControl.Host.Services.Launch;

public static class ShotNormalizer
{
    public const double MphToMps = 0.44704;

    public static BayShot FromNovaJson(JsonElement root, DateTimeOffset? receivedAt = null)
    {
        var ball = new BallMetrics(
            SpeedMps: ReadDouble(root, "ball_speed_meters_per_second") ?? 0,
            VlaDeg: ReadDouble(root, "vertical_launch_angle_degrees") ?? 0,
            HlaDeg: ReadDouble(root, "horizontal_launch_angle_degrees") ?? 0,
            TotalSpinRpm: ReadDouble(root, "total_spin_rpm") ?? 0,
            SpinAxisDeg: ReadDouble(root, "spin_axis_degrees") ?? 0,
            BackSpinRpm: ReadDouble(root, "back_spin_rpm"),
            SideSpinRpm: ReadDouble(root, "side_spin_rpm"));

        int? shotNumber = null;
        if (root.TryGetProperty("shot_number", out var sn) && sn.TryGetInt32(out var n))
        {
            shotNumber = n;
        }

        return new BayShot(
            Id: Guid.NewGuid().ToString("N"),
            ReceivedAt: receivedAt ?? DateTimeOffset.UtcNow,
            Source: "nova",
            DeviceId: ReadString(root, "device_id") ?? ReadString(root, "serial"),
            SourceShotNumber: shotNumber,
            Ball: ball,
            Club: null,
            CarryMeters: ReadDouble(root, "carry_meters") ?? ReadDouble(root, "carry_estimate_meters"),
            UnitsHint: "si");
    }

    /// <summary>
    /// Open Connect v1 ball speed is mph; store SI (m/s).
    /// </summary>
    public static BayShot FromOpenConnectBallData(JsonElement root, DateTimeOffset? receivedAt = null)
    {
        // Typical shape: { "BallData": { "BallSpeed": mph, "LaunchAngle": …, "LaunchDirection": …, "TotalSpin": …, "SpinAxis": … } }
        var ballEl = root.TryGetProperty("BallData", out var bd) ? bd : root;

        var speedMph = ReadDouble(ballEl, "BallSpeed") ?? ReadDouble(ballEl, "ballSpeed") ?? 0;
        var vla = ReadDouble(ballEl, "LaunchAngle") ?? ReadDouble(ballEl, "VLA") ?? 0;
        var hla = ReadDouble(ballEl, "LaunchDirection") ?? ReadDouble(ballEl, "HLA") ?? 0;
        var spin = ReadDouble(ballEl, "TotalSpin") ?? ReadDouble(ballEl, "SpinRate") ?? 0;
        var axis = ReadDouble(ballEl, "SpinAxis") ?? 0;

        var units = ReadString(root, "Units") ?? "Yards";

        return new BayShot(
            Id: Guid.NewGuid().ToString("N"),
            ReceivedAt: receivedAt ?? DateTimeOffset.UtcNow,
            Source: "open_connect",
            DeviceId: null,
            SourceShotNumber: null,
            Ball: new BallMetrics(
                SpeedMps: speedMph * MphToMps,
                VlaDeg: vla,
                HlaDeg: hla,
                TotalSpinRpm: spin,
                SpinAxisDeg: axis),
            Club: null,
            CarryMeters: null,
            UnitsHint: units);
    }

    public static BayShot Simulate(
        double speedMph = 145,
        double vla = 12.5,
        double hla = -1.2,
        double spin = 2700,
        double axis = -3.0)
    {
        return new BayShot(
            Id: Guid.NewGuid().ToString("N"),
            ReceivedAt: DateTimeOffset.UtcNow,
            Source: "manual",
            DeviceId: "simulate",
            SourceShotNumber: null,
            Ball: new BallMetrics(
                SpeedMps: speedMph * MphToMps,
                VlaDeg: vla,
                HlaDeg: hla,
                TotalSpinRpm: spin,
                SpinAxisDeg: axis),
            UnitsHint: "simulated_mph");
    }

    public static double MpsToMph(double mps) => mps / MphToMps;
    public static double MetersToYards(double m) => m * 1.0936133;

    private static double? ReadDouble(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetDouble(out var d) ? d : null,
            JsonValueKind.String when double.TryParse(p.GetString(), out var d) => d,
            _ => null
        };
    }

    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
