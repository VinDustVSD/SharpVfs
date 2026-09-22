using VinDust.SharpVfs.Abstractions;

namespace VinDust.SharpVfs.FileSystems.Zip;

/// <summary>Options controlling the behavior of a <see cref="ZipArchiveFileSystem"/>.</summary>
public sealed class ZipFileSystemOptions : VfsBehaviorOptions
{
    /// <summary>Default options.</summary>
    public static readonly ZipFileSystemOptions Default = new();

    /// <summary>Initializes a new instance of the <see cref="ZipFileSystemOptions"/> class.</summary>
    public ZipFileSystemOptions()
    {
    }
}