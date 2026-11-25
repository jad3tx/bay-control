using BayControl.Host.Models;

namespace BayControl.Host.Services.Launch;

public static class ShotDto
{
    public static object ToClient(BayShot shot, string speedUnit = "mph", string distanceUnit = "yards")
    {
        var useMph = !string.Equals(speedUnit, "mps", StringComparison.OrdinalIgnoreCase);
        var useYards = !string.Equals(distanceUnit, "meters", StringComparison.OrdinalIgnoreCase);

        double speed = useMph ? ShotNormalizer.MpsToMph(shot.Ball.SpeedMps) : shot.Ball.SpeedMps;
        double? carry = shot.CarryMeters is null
            ? null
            : useYards
                ? ShotNormalizer.MetersToYards(shot.CarryMeters.Value)
                : shot.CarryMeters;

        return new
        {
            id = shot.Id,
            receivedAt = shot.ReceivedAt,
            source = shot.Source,
            deviceId = shot.DeviceId,
            sourceShotNumber = shot.SourceShotNumber,
            ball = new
            {
                speed,
                speedUnit = useMph ? "mph" : "m/s",
                vlaDeg = shot.Ball.VlaDeg,
                hlaDeg = shot.Ball.HlaDeg,
                totalSpinRpm = shot.Ball.TotalSpinRpm,
                spinAxisDeg = shot.Ball.SpinAxisDeg,
                backSpinRpm = shot.Ball.BackSpinRpm,
                sideSpinRpm = shot.Ball.SideSpinRpm
            },
            club = shot.Club,
            carry,
            carryUnit = carry is null ? null : useYards ? "yards" : "meters",
            unitsHint = shot.UnitsHint,
            clips = shot.Clips
        };
    }
}
