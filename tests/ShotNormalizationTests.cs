using System.Text.Json;
using BayControl.Host.Services.Launch;

namespace BayControl.Tests;

public class ShotNormalizationTests
{
    [Fact]
    public void FromNovaJson_maps_si_fields()
    {
        using var doc = JsonDocument.Parse("""
        {
          "type": "shot",
          "shot_number": 42,
          "ball_speed_meters_per_second": 62.5,
          "vertical_launch_angle_degrees": 14.2,
          "horizontal_launch_angle_degrees": -1.3,
          "total_spin_rpm": 2850,
          "spin_axis_degrees": -4.5
        }
        """);

        var shot = ShotNormalizer.FromNovaJson(doc.RootElement);

        Assert.Equal("nova", shot.Source);
        Assert.Equal(42, shot.SourceShotNumber);
        Assert.Equal(62.5, shot.Ball.SpeedMps, 3);
        Assert.Equal(14.2, shot.Ball.VlaDeg, 3);
        Assert.Equal(-1.3, shot.Ball.HlaDeg, 3);
        Assert.Equal(2850, shot.Ball.TotalSpinRpm, 1);
        Assert.Equal(-4.5, shot.Ball.SpinAxisDeg, 3);
    }

    [Fact]
    public void FromOpenConnect_converts_mph_to_mps()
    {
        using var doc = JsonDocument.Parse("""
        {
          "Units": "Yards",
          "BallData": {
            "BallSpeed": 100,
            "LaunchAngle": 12,
            "LaunchDirection": -2,
            "TotalSpin": 2500,
            "SpinAxis": 1.5
          }
        }
        """);

        var shot = ShotNormalizer.FromOpenConnectBallData(doc.RootElement);

        Assert.Equal("open_connect", shot.Source);
        Assert.Equal(100 * ShotNormalizer.MphToMps, shot.Ball.SpeedMps, 5);
        Assert.Equal(12, shot.Ball.VlaDeg, 3);
        Assert.Equal(-2, shot.Ball.HlaDeg, 3);
    }

    [Fact]
    public void History_limit_evicts_oldest()
    {
        var ingest = new ShotIngestService();
        ingest.SetHistoryLimit(3);

        for (var i = 0; i < 5; i++)
        {
            ingest.Ingest(ShotNormalizer.Simulate(speedMph: 100 + i));
        }

        var recent = ingest.GetRecent(10);
        Assert.Equal(3, recent.Count);
        Assert.Equal(104 * ShotNormalizer.MphToMps, recent[0].Ball.SpeedMps, 3);
        Assert.Equal(102 * ShotNormalizer.MphToMps, recent[2].Ball.SpeedMps, 3);
    }

    [Fact]
    public void MpsToMph_roundtrips()
    {
        var mph = 145.0;
        var mps = mph * ShotNormalizer.MphToMps;
        Assert.Equal(mph, ShotNormalizer.MpsToMph(mps), 5);
    }
}
