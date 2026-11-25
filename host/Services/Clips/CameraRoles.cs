namespace BayControl.Host.Services.Clips;

public static class CameraRoles
{
    public const string FaceOn = "face_on";
    public const string DownTheLine = "down_the_line";
    public const string ClubFace = "club_face";

    public static string DisplayName(string role) => role switch
    {
        FaceOn => "Face-on",
        DownTheLine => "Down-the-line",
        ClubFace => "Club face",
        _ => role
    };
}

public sealed record VideoDeviceInfo(string Id, string Name, int Index);

public sealed record ClipManifestEntry(
    string Id,
    string ShotId,
    string CameraRole,
    DateTimeOffset CreatedAt,
    int DurationMs,
    long SizeBytes,
    int ImpactOffsetMs,
    string FileName);
