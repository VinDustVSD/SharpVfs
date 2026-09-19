using System;
using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core;

/// <summary>
/// Pairs a <see cref="FileSystemEntry"/> with the <see cref="VfsUri"/> at which it
/// was observed. This is the type returned by <see cref="VfsRoot"/> to end users.
/// </summary>
public sealed class VfsEntry
{
    internal VfsEntry(VfsUri uri, FileSystemEntry entry)
    {
        Uri = uri;
        Entry = entry;
    }

    /// <summary>Gets the URI of this entry within the VFS tree.</summary>
    public VfsUri Uri { get; }

    /// <summary>Gets the underlying file system entry.</summary>
    public FileSystemEntry Entry { get; }

    /// <summary>Gets the path of this entry within the owning file system.</summary>
    public FsPath Path => Entry.Path;

    /// <summary>Gets the size of the entry in bytes.</summary>
    public long Size => Entry.Size;

    /// <summary>Gets the time the entry was last modified.</summary>
    public DateTimeOffset LastModified => Entry.LastModified;

    /// <summary>Gets the time the entry was created, if known.</summary>
    public DateTimeOffset Created => Entry.Created;

    /// <summary>Gets the time the entry was last accessed, if known.</summary>
    public DateTimeOffset Accessed => Entry.Accessed;

    /// <summary>Gets the file attributes of the entry.</summary>
    public System.IO.FileAttributes Attributes => Entry.Attributes;

    /// <summary>Gets the MIME content type of the entry, if known.</summary>
    public string? ContentType => Entry.ContentType;

    /// <summary>Gets the eager content hash recorded by the owning file system, if any.</summary>
    /// <remarks>
    /// This property never triggers I/O. To compute a hash on demand through
    /// <see cref="Abstractions.IContentHashProvider"/>, call
    /// <see cref="VfsRoot.GetContentHashAsync"/>.
    /// </remarks>
    public string? ContentHash => Entry.ContentHash;

    /// <summary>Gets a value indicating whether this entry is a regular file.</summary>
    public bool IsFile => Entry is FileEntry;

    /// <summary>Gets a value indicating whether this entry is a directory.</summary>
    public bool IsDirectory => Entry is DirectoryEntry;

    /// <summary>Gets a value indicating whether this entry is a symbolic link.</summary>
    public bool IsSymbolicLink => Entry is SymbolicLink;
}