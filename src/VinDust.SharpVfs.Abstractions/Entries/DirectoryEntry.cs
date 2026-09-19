using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Abstractions.Entries;

/// <summary>Represents a directory.</summary>
public sealed class DirectoryEntry : FileSystemEntry
{
    /// <summary>Initializes a new instance of the <see cref="DirectoryEntry"/> class.</summary>
    /// <param name="path">The path of this entry within the owning file system.</param>
    public DirectoryEntry(FsPath path)
        : base(path)
    {
    }
}