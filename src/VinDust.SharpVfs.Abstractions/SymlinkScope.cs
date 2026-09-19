namespace VinDust.SharpVfs.Abstractions;

/// <summary>Specifies the boundary within which symbolic links may resolve.</summary>
public enum SymlinkScope
{
    /// <summary>A symbolic link must resolve within the same file system instance.</summary>
    SameFs,

    /// <summary>
    /// A symbolic link may resolve to any file system reachable under the same
    /// <c>VfsRoot</c>, including file systems registered under different schemes.
    /// </summary>
    SameRoot,
}