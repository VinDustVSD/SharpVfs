using VinDust.SharpVfs.Abstractions;

namespace VinDust.SharpVfs.Core.Internal;

/// <summary>
/// The internal representation of a mount point stored in the trie. Wraps the public
/// <see cref="MountPoint"/> so that the trie does not need to know about refcounting.
/// </summary>
internal sealed class MountEntry
{
    public MountEntry(MountPoint point)
    {
        Point = point;
    }

    public MountPoint Point { get; }
}
