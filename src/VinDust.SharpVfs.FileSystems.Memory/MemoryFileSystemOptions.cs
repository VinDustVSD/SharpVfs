using VinDust.SharpVfs.Abstractions;

namespace VinDust.SharpVfs.FileSystems.Memory;

/// <summary>Options controlling the behavior of a <see cref="MemoryFileSystem"/>.</summary>
public sealed class MemoryFileSystemOptions : VfsBehaviorOptions
{
    /// <summary>Default options.</summary>
    public static readonly MemoryFileSystemOptions Default = new();

    /// <summary>Initializes a new instance of the <see cref="MemoryFileSystemOptions"/> class.</summary>
    public MemoryFileSystemOptions()
    {
    }
}