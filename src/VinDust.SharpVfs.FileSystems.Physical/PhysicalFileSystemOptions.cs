using VinDust.SharpVfs.Abstractions;

namespace VinDust.SharpVfs.FileSystems.Physical;

/// <summary>Options controlling the behavior of a <see cref="PhysicalFileSystem"/>.</summary>
public sealed class PhysicalFileSystemOptions : VfsBehaviorOptions
{
    /// <summary>Default options.</summary>
    public static readonly PhysicalFileSystemOptions Default = new();

    /// <summary>Initializes a new instance of the <see cref="PhysicalFileSystemOptions"/> class.</summary>
    public PhysicalFileSystemOptions()
    {
    }

    /// <summary>
    /// Gets a value indicating whether the root directory is created if it does not exist.
    /// When <see langword="false"/> (default), constructing a file system over a missing
    /// root throws <see cref="System.IO.DirectoryNotFoundException"/>.
    /// </summary>
    public bool CreateRootIfMissing { get; init; }
}