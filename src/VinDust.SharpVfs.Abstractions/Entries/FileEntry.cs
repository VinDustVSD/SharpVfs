using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Abstractions.Entries;

/// <summary>Represents a regular file.</summary>
public sealed class FileEntry : FileSystemEntry
{
    /// <summary>Initializes a new instance of the <see cref="FileEntry"/> class.</summary>
    /// <param name="path">The path of this entry within the owning file system.</param>
    public FileEntry(FsPath path)
        : base(path)
    {
    }
}
