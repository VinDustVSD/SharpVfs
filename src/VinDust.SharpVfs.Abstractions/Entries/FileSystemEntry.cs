using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Abstractions.Entries;

/// <summary>
/// Base type for any entry known to a file system: a file, a directory, or a symbolic link.
/// </summary>
/// <remarks>
/// Entries carry metadata captured at the moment they were produced by
/// <c>GetEntryAsync</c> or <c>EnumerateAsync</c>. Metadata may become stale if the
/// underlying storage changes; callers that need fresh data should query again.
/// </remarks>
public abstract class FileSystemEntry
{
    /// <summary>Initializes a new instance of the <see cref="FileSystemEntry"/> class.</summary>
    /// <param name="path">The path of this entry within the owning file system.</param>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="path"/> is the default value.</exception>
    protected FileSystemEntry(FsPath path)
    {
        if (path.IsDefault)
        {
            throw new System.ArgumentException("Path must be initialized.", nameof(path));
        }

        Path = path;
    }

    /// <summary>Gets the path of this entry within the owning file system.</summary>
    public FsPath Path { get; }

    /// <summary>Gets the size of the entry in bytes, or <c>0</c> for directories.</summary>
    public long Size { get; init; }

    /// <summary>Gets the time the entry was last modified.</summary>
    public DateTimeOffset LastModified { get; init; }

    /// <summary>Gets the time the entry was created, if known.</summary>
    public DateTimeOffset Created { get; init; }

    /// <summary>Gets the time the entry was last accessed, if known.</summary>
    public DateTimeOffset Accessed { get; init; }

    /// <summary>Gets the file attributes of the entry.</summary>
    public System.IO.FileAttributes Attributes { get; init; }

    /// <summary>Gets the MIME content type of the entry, if known.</summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Gets the content hash of the entry in opaque form, if known. Format is
    /// <c>&lt;algorithm&gt;:&lt;hex&gt;</c>, for example <c>crc32:abcdef01</c> or
    /// <c>sha256:...</c>. File systems that can compute the hash cheaply populate
    /// this value eagerly; others leave it <see langword="null"/> and rely on
    /// <c>IContentHashProvider</c> at a higher layer.
    /// </summary>
    public string? ContentHash { get; init; }
}
