namespace AppleMusicHistory.Core.Models;

public sealed record AppRunInfo(
    DateTimeOffset StartedAtUtc,
    string AppVersion,
    string MachineName,
    string UserName,
    string Runtime,
    string OsVersion);
