namespace BayControl.Host.Models;

public sealed record BallMetrics(
    double SpeedMps,
    double VlaDeg,
    double HlaDeg,
    double TotalSpinRpm,
    double SpinAxisDeg,
    double? BackSpinRpm = null,
    double? SideSpinRpm = null);

public sealed record ClubMetrics(
    double? SpeedMps = null,
    double? AttackAngleDeg = null,
    double? FaceAngleDeg = null,
    double? PathDeg = null);

public sealed record ClipLink(
    string ClipId,
    string? CameraRole = null,
    int? ImpactOffsetMs = null);

public sealed record BayShot(
    string Id,
    DateTimeOffset ReceivedAt,
    string Source,
    string? DeviceId,
    int? SourceShotNumber,
    BallMetrics Ball,
    ClubMetrics? Club = null,
    double? CarryMeters = null,
    string? UnitsHint = null,
    IReadOnlyList<ClipLink>? Clips = null);

public sealed record AdapterStatus(
    string State,
    string? Detail = null,
    string? Firmware = null,
    int? ShotCount = null,
    DateTimeOffset? LastMessageAt = null);
