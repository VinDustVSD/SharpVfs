namespace VinDust.SharpVfs.Abstractions;

/// <summary>
/// Describes the set of operations supported by a file system implementation.
/// </summary>
/// <remarks>
/// Capabilities are advisory hints used by the resolver, CLI tooling, and callers to
/// avoid invoking operations that are guaranteed to fail. Calling an unsupported
/// operation still throws <see cref="System.NotSupportedException"/>; capabilities
/// exist to let callers branch earlier without a failed round trip.
/// </remarks>
[Flags]
public enum FileSystemCapabilities
{
    /// <summary>No capabilities. An instance with this value cannot be used for I/O.</summary>
    None = 0,

    /// <summary>The file system supports read operations.</summary>
    Read = 1 << 0,

    /// <summary>The file system supports write operations.</summary>
    Write = 1 << 1,

    /// <summary>The file system supports directory creation.</summary>
    CreateDirectory = 1 << 2,

    /// <summary>The file system supports deleting files and directories.</summary>
    Delete = 1 << 3,

    /// <summary>The file system supports moving entries.</summary>
    Move = 1 << 4,

    /// <summary>The file system supports copying entries.</summary>
    Copy = 1 << 5,

    /// <summary>The file system supports symbolic links.</summary>
    Symlinks = 1 << 6,

    /// <summary>The file system implements <c>IPatchableContainerFileSystem</c>.</summary>
    PatchableContainer = 1 << 7,

    /// <summary><c>OpenReadAsync</c> returns a seekable stream.</summary>
    Seekable = 1 << 8,
}
