using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core;

/// <summary>
/// The result of resolving a path through a chain of mount points.
/// </summary>
public readonly struct ResolvedPath
{
    /// <summary>Initializes a new instance of the <see cref="ResolvedPath"/> struct.</summary>
    /// <param name="fileSystem">The file system that owns the resolved path.</param>
    /// <param name="path">The path within <paramref name="fileSystem"/>.</param>
    /// <param name="readOnly">Whether the resolution passed through a read-only mount.</param>
    public ResolvedPath(IFileSystem fileSystem, FsPath path, bool readOnly)
    {
        FileSystem = fileSystem;
        Path = path;
        ReadOnly = readOnly;
    }

    /// <summary>Gets the file system that owns the resolved path.</summary>
    public IFileSystem FileSystem { get; }

    /// <summary>Gets the path within <see cref="FileSystem"/>.</summary>
    public FsPath Path { get; }

    /// <summary>
    /// Gets a value indicating whether the resolution passed through at least one
    /// mount point marked read-only.
    /// </summary>
    public bool ReadOnly { get; }
}