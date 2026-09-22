using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core;

/// <summary>
/// Describes the outcome of resolving a <see cref="VfsUri"/> to a concrete file system
/// and path, including the chain of mount points crossed during resolution.
/// </summary>
/// <remarks>
/// <para>
/// Resolution walks from the file system registered for the URI's scheme, following
/// the longest matching mount point at each step, until no further mount applies.
/// The final <see cref="FileSystem"/> is the deepest target, and <see cref="Path"/> is
/// the remainder within it.
/// </para>
/// <para>
/// This type is informational. It does not perform I/O or dispatch operations; it
/// reports where a URI <em>would</em> resolve. Use it for diagnostics, tooling, or
/// callers that need to know the mount chain — for example, delta builders that
/// patch entries inside archives.
/// </para>
/// </remarks>
public sealed class VfsResolution
{
    internal VfsResolution(
        IFileSystem fileSystem,
        FsPath path,
        IReadOnlyList<MountPoint> mountChain,
        bool readOnly)
    {
        FileSystem = fileSystem;
        Path = path;
        MountChain = mountChain;
        ReadOnly = readOnly;
    }

    /// <summary>Gets the file system that owns the resolved path.</summary>
    public IFileSystem FileSystem { get; }

    /// <summary>Gets the path within <see cref="FileSystem"/>.</summary>
    public FsPath Path { get; }

    /// <summary>
    /// Gets the mount points crossed during resolution, ordered outermost-first. Empty
    /// when the URI resolved entirely within the scheme's file system.
    /// </summary>
    public IReadOnlyList<MountPoint> MountChain { get; }

    /// <summary>
    /// Gets a value indicating whether any mount point in the chain was marked read-only.
    /// </summary>
    public bool ReadOnly { get; }

    /// <summary>Gets a value indicating whether the resolution crossed at least one mount point.</summary>
    public bool CrossedMounts => MountChain.Count > 0;
}