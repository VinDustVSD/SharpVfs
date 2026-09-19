using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core;

/// <summary>Describes a single position in a resolution walk, for tracing purposes.</summary>
public readonly struct ResolveStep
{
    /// <summary>Initializes a new instance of the <see cref="ResolveStep"/> struct.</summary>
    /// <param name="path">The path at this step.</param>
    /// <param name="fileSystem">The file system currently resolving.</param>
    /// <param name="depth">The zero-based depth of this step in the resolution walk.</param>
    public ResolveStep(FsPath path, IFileSystem fileSystem, int depth)
    {
        Path = path;
        FileSystem = fileSystem;
        Depth = depth;
    }

    /// <summary>Gets the path at this step.</summary>
    public FsPath Path { get; }

    /// <summary>Gets the file system currently resolving.</summary>
    public IFileSystem FileSystem { get; }

    /// <summary>Gets the zero-based depth of this step.</summary>
    public int Depth { get; }
}
