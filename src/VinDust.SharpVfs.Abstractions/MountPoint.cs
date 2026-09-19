using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Abstractions;

/// <summary>Describes a single mount point within a file system.</summary>
/// <param name="Path">The mount point path within the hosting file system.</param>
/// <param name="Target">The file system mounted at <paramref name="Path"/>.</param>
/// <param name="Options">The options in effect for this mount.</param>
/// <param name="MountedAt">The time at which the mount was established.</param>
public sealed record MountPoint(
    FsPath Path,
    IFileSystem Target,
    MountOptions Options,
    DateTimeOffset MountedAt);