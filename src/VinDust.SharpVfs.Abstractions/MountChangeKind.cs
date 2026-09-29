namespace VinDust.SharpVfs.Abstractions;

/// <summary>Identifies the kind of change that occurred to a mount table.</summary>
public enum MountChangeKind
{
    /// <summary>A new mount point was added.</summary>
    Added,

    /// <summary>An existing mount point was removed.</summary>
    Removed,

    /// <summary>An existing mount point was replaced by a new one at the same path.</summary>
    Replaced,
}