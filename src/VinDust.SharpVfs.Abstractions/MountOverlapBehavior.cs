namespace VinDust.SharpVfs.Abstractions;

/// <summary>Specifies how a mount point interacts with existing content at the same path.</summary>
public enum MountOverlapBehavior
{
    /// <summary>
    /// The mounted file system fully replaces the subtree under the mount point.
    /// Content from the base file system under the mount point is hidden, including
    /// any entries below the mount path.
    /// </summary>
    Replace,

    /// <summary>
    /// Content from the base file system remains visible under the mount point.
    /// Reads fall through to the base when the mounted file system has no entry;
    /// on name conflicts, the mounted file system wins.
    /// </summary>
    Overlay,
}