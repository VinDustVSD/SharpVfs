using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.FileSystems.Zip;

/// <summary>
/// A file system backed by a ZIP archive, with read and write support.
/// </summary>
/// <remarks>
/// <para>
/// The archive is fully loaded into memory at construction. This makes reads
/// seekable and writes straightforward, at the cost of memory proportional to the
/// uncompressed archive size. For typical mod and asset archives this is acceptable;
/// streaming storage for very large archives is a follow-up.
/// </para>
/// <para>
/// Writes are applied via a full repack of the archive: the in-memory content is
/// re-serialized to the underlying stream. This is honest about cost: ZIP's central
/// directory structure technically permits in-place updates of individual entries,
/// but the .NET BCL <see cref="ZipArchive"/> does not expose that capability.
/// In-place patching is a follow-up that will require a hand-written ZIP writer.
/// </para>
/// <para>
/// For batch writes, use <see cref="BeginWriteSession"/>: mutations are accumulated
/// in an overlay and applied in a single repack on commit. Without a session, each
/// mutation triggers an immediate repack.
/// </para>
/// <para>
/// The file system is writable only when the source stream is writable and seekable.
/// Otherwise write operations throw <see cref="VfsReadOnlyException"/>.
/// </para>
/// </remarks>
public sealed class ZipArchiveFileSystem : IFileSystem, IPatchableContainerFileSystem
{
    private readonly Stream _source;
    private readonly bool _leaveOpen;
    private readonly ZipFileSystemOptions _options;
    private readonly DateTimeOffset _constructedAt = DateTimeOffset.UtcNow;
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _fileTimes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _directoryTimes = new(StringComparer.Ordinal);
    private readonly List<string> _rawEntryNames = [];
    private readonly bool _writable;

    private WriteSessionState? _activeSession;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ZipArchiveFileSystem"/> class over the archive at the specified path.</summary>
    /// <param name="path">The path of the ZIP archive on disk.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the archive does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the file is not a valid ZIP archive.</exception>
    public ZipArchiveFileSystem(string path)
        : this(path, ZipFileSystemOptions.Default)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ZipArchiveFileSystem"/> class over the archive at the specified path.</summary>
    /// <param name="path">The path of the ZIP archive on disk.</param>
    /// <param name="options">The behavior options.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the archive does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the file is not a valid ZIP archive.</exception>
    public ZipArchiveFileSystem(string path, ZipFileSystemOptions options)
        : this(OpenFile(path), leaveOpen: false, options)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ZipArchiveFileSystem"/> class over the specified stream.</summary>
    /// <param name="stream">The stream containing ZIP data.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="stream"/> is not readable.</exception>
    /// <exception cref="InvalidDataException">Thrown when the stream does not contain a valid ZIP archive.</exception>
    public ZipArchiveFileSystem(Stream stream)
        : this(stream, leaveOpen: false, ZipFileSystemOptions.Default)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ZipArchiveFileSystem"/> class over the specified stream.</summary>
    /// <param name="stream">The stream containing ZIP data.</param>
    /// <param name="options">The behavior options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="stream"/> is not readable.</exception>
    /// <exception cref="InvalidDataException">Thrown when the stream does not contain a valid ZIP archive.</exception>
    public ZipArchiveFileSystem(Stream stream, ZipFileSystemOptions options)
        : this(stream, leaveOpen: false, options)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ZipArchiveFileSystem"/> class over the specified stream.</summary>
    /// <param name="stream">The stream containing ZIP data.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream is not disposed when the file system is disposed.</param>
    /// <param name="options">The behavior options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="stream"/> is not readable.</exception>
    /// <exception cref="InvalidDataException">Thrown when the stream does not contain a valid ZIP archive.</exception>
    public ZipArchiveFileSystem(Stream stream, bool leaveOpen, ZipFileSystemOptions options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);

        if (!stream.CanRead)
        {
            throw new ArgumentException("Stream must be readable.", nameof(stream));
        }

        _source = stream;
        _leaveOpen = leaveOpen;
        _options = options;
        _writable = stream.CanWrite && stream.CanSeek;

        LoadArchive();
    }

    /// <inheritdoc />
    public FileSystemCapabilities Capabilities =>
        _writable
            ? FileSystemCapabilities.Read
            | FileSystemCapabilities.Write
            | FileSystemCapabilities.CreateDirectory
            | FileSystemCapabilities.Delete
            | FileSystemCapabilities.Move
            | FileSystemCapabilities.Copy
            | FileSystemCapabilities.Seekable
            | FileSystemCapabilities.PatchableContainer
            : FileSystemCapabilities.Read | FileSystemCapabilities.Seekable;

    /// <summary>Gets the raw ZIP entry names as stored in the archive.</summary>
    /// <returns>A read-only list of exact entry names as stored in the archive.</returns>
    public IReadOnlyList<string> GetRawEntryNames()
    {
        ThrowIfDisposed();
        return _rawEntryNames.AsReadOnly();
    }

    /// <summary>Opens a raw ZIP entry by its exact archive name, bypassing path validation.</summary>
    /// <param name="fullName">The exact entry name as stored in the ZIP central directory.</param>
    /// <returns>A readable stream over the entry content.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fullName"/> is <see langword="null"/>.</exception>
    /// <exception cref="VfsNotFoundException">Thrown when no entry with the specified name exists.</exception>
    public Stream OpenRawEntry(string fullName)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(fullName);

        var normalized = NormalizeEntryName(fullName) ?? throw new VfsNotFoundException($"Raw entry has an invalid name: '{fullName}'.");
        var key = "/" + normalized.TrimEnd('/');
        var view = GetView();
        if (view.Files.TryGetValue(key, out var content))
        {
            return new MemoryStream(content, writable: false);
        }

        throw new VfsNotFoundException($"Raw ZIP entry not found: '{fullName}'.");
    }

    /// <inheritdoc />
    public ValueTask<bool> ExistsAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();

        var key = path.ToString();
        var view = GetView();
        return new(view.Files.ContainsKey(key) || IsDirectory(key, view));
    }

    /// <inheritdoc />
    public ValueTask<FileSystemEntry?> GetEntryAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();

        var key = path.ToString();
        var view = GetView();

        if (view.Files.TryGetValue(key, out var content))
        {
            FileSystemEntry entry = MaterializeFile(key, content, view);
            return new(entry);
        }

        if (IsDirectory(key, view))
        {
            FileSystemEntry entry = MaterializeDirectory(key, view);
            return new(entry);
        }

        return new((FileSystemEntry?)null);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<FileSystemEntry> EnumerateAsync(
        FsPath path,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);

        var key = path.ToString();
        var view = GetView();

        if (!IsDirectory(key, view))
        {
            if (view.Files.ContainsKey(key))
            {
                throw new VfsNotDirectoryException($"'{path}' is not a directory.");
            }

            throw new VfsNotDirectoryException($"Directory not found: '{path}'.");
        }

        var children = CollectChildren(key, view);

        foreach (var (childKey, isDirectory) in children)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();

            if (isDirectory)
            {
                yield return MaterializeDirectory(childKey, view);
            }
            else
            {
                yield return MaterializeFile(childKey, view.Files[childKey], view);
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<Stream> OpenReadAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();

        var key = path.ToString();
        var view = GetView();

        if (view.Files.TryGetValue(key, out var content))
        {
            return new(new MemoryStream(content, writable: false));
        }

        if (IsDirectory(key, view))
        {
            throw new VfsNotDirectoryException($"'{path}' is a directory.");
        }

        throw new VfsNotFoundException($"File not found: '{path}'.");
    }

    /// <inheritdoc />
    public ValueTask<Stream> OpenWriteAsync(
        FsPath path,
        FileWriteMode mode = FileWriteMode.Create,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();
        ThrowIfNotWritable();

        if (path.IsRoot)
        {
            throw new VfsNotDirectoryException("Cannot open the root directory for writing.");
        }

        var view = GetView();
        var key = path.ToString();
        view.Files.TryGetValue(key, out var existing);
        view.FileTimes.TryGetValue(key, out var existingTime);

        switch (mode)
        {
            case FileWriteMode.Create:
                break;

            case FileWriteMode.CreateExclusive:
                if (view.Files.ContainsKey(key) || IsDirectory(key, view))
                {
                    throw new VfsNotDirectoryException($"'{path}' already exists.");
                }

                break;

            case FileWriteMode.Append:
                if (IsDirectory(key, view))
                {
                    throw new VfsNotDirectoryException($"'{path}' is a directory.");
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown write mode.");
        }

        var initial = mode switch
        {
            FileWriteMode.Append when existing is not null => (byte[])existing.Clone(),
            FileWriteMode.Create when existing is not null => existing,
            _ => [],
        };

        EnsureAncestors(key, view);

        var buffer = new MemoryStream();
        if (mode == FileWriteMode.Append && initial.Length > 0)
        {
            buffer.Write(initial, 0, initial.Length);
        }
        else if (mode != FileWriteMode.Append)
        {
            // Create/CreateNew start empty.
            buffer.SetLength(0);
        }

        buffer.Position = buffer.Length;

        var mtime = DateTimeOffset.UtcNow;
        return new(new PendingWriteStream(
            buffer,
            bytes => CommitWrite(view, key, bytes, mtime),
            OnStreamOpened,
            OnStreamClosed));
    }

    /// <inheritdoc />
    public ValueTask CreateDirectoryAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();
        ThrowIfNotWritable();

        var key = path.ToString();
        var view = GetView();

        if (key == "/")
        {
            return default;
        }

        if (IsDirectory(key, view))
        {
            if (_options.DirectoryExistsBehavior == DirectoryExistsBehavior.Throw)
            {
                throw new VfsNotDirectoryException($"Directory already exists: '{path}'.");
            }

            return default;
        }

        if (view.Files.ContainsKey(key))
        {
            throw new VfsNotDirectoryException($"'{path}' already exists and is not a directory.");
        }

        EnsureAncestors(key, view);
        view.Directories.Add(key);
        view.DirectoryTimes[key] = DateTimeOffset.UtcNow;

        OnMutationApplied();
        return default;
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(FsPath path, bool recursive = false, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();
        ThrowIfNotWritable();

        if (path.IsRoot)
        {
            throw new VfsNotDirectoryException("Cannot delete the root directory.");
        }

        var key = path.ToString();
        var view = GetView();

        if (view.Files.Remove(key))
        {
            view.FileTimes.Remove(key);
            OnMutationApplied();
            return default;
        }

        if (IsDirectory(key, view))
        {
            var descendants = CollectDescendantPaths(key, view);

            if (descendants.Count > 0 && !recursive)
            {
                throw new VfsNotDirectoryException(
                    $"Directory '{path}' is not empty. Pass recursive: true to delete it.");
            }

            foreach (var descendant in descendants)
            {
                view.Files.Remove(descendant);
                view.FileTimes.Remove(descendant);
                view.Directories.Remove(descendant);
                view.DirectoryTimes.Remove(descendant);
            }

            view.Directories.Remove(key);
            view.DirectoryTimes.Remove(key);
            OnMutationApplied();
            return default;
        }

        if (_options.DeleteBehavior == DeleteBehavior.Throw)
        {
            throw new VfsNotFoundException($"Path not found: '{path}'.");
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask MoveAsync(FsPath source, FsPath destination, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(source);
        ValidatePath(destination);
        ct.ThrowIfCancellationRequested();
        ThrowIfNotWritable();

        if (source.IsRoot || destination.IsRoot)
        {
            throw new VfsNotDirectoryException("Cannot move the root directory.");
        }

        if (source == destination)
        {
            return default;
        }

        var sourceKey = source.ToString();
        var destKey = destination.ToString();
        var view = GetView();

        if (destination.StartsWith(source))
        {
            throw new VfsNotDirectoryException(
                $"Cannot move '{source}' into its own subtree '{destination}'.");
        }

        if (view.Files.TryGetValue(sourceKey, out var fileContent))
        {
            if (view.Files.ContainsKey(destKey) || IsDirectory(destKey, view))
            {
                throw new VfsNotDirectoryException($"Destination already exists: '{destination}'.");
            }

            view.Files.Remove(sourceKey);
            view.Files[destKey] = fileContent;

            if (view.FileTimes.Remove(sourceKey, out var mtime))
            {
                view.FileTimes[destKey] = mtime;
            }

            EnsureAncestors(destKey, view);
            OnMutationApplied();
            return default;
        }

        if (IsDirectory(sourceKey, view))
        {
            var descendants = CollectDescendantPaths(sourceKey, view);
            if (descendants.Count > 0)
            {
                throw new NotSupportedException(
                    "Moving non-empty directories is not supported by ZipArchiveFileSystem. " +
                    "Move the entries individually or use a composition decorator.");
            }

            if (view.Files.ContainsKey(destKey) || IsDirectory(destKey, view))
            {
                throw new VfsNotDirectoryException($"Destination already exists: '{destination}'.");
            }

            view.Directories.Remove(sourceKey);
            view.DirectoryTimes.Remove(sourceKey);
            EnsureAncestors(destKey, view);
            view.Directories.Add(destKey);
            view.DirectoryTimes[destKey] = DateTimeOffset.UtcNow;
            OnMutationApplied();
            return default;
        }

        throw new VfsNotFoundException($"Source not found: '{source}'.");
    }

    /// <inheritdoc />
    public ValueTask CopyAsync(FsPath source, FsPath destination, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(source);
        ValidatePath(destination);
        ct.ThrowIfCancellationRequested();
        ThrowIfNotWritable();

        if (source.IsRoot || destination.IsRoot)
        {
            throw new VfsNotDirectoryException("Cannot copy the root directory.");
        }

        if (source == destination)
        {
            return default;
        }

        var sourceKey = source.ToString();
        var destKey = destination.ToString();
        var view = GetView();

        if (destination.StartsWith(source))
        {
            throw new VfsNotDirectoryException(
                $"Cannot copy '{source}' into its own subtree '{destination}'.");
        }

        if (view.Files.TryGetValue(sourceKey, out var fileContent))
        {
            if (view.Files.ContainsKey(destKey) || IsDirectory(destKey, view))
            {
                throw new VfsNotDirectoryException($"Destination already exists: '{destination}'.");
            }

            view.Files[destKey] = (byte[])fileContent.Clone();
            view.FileTimes[destKey] = view.FileTimes.TryGetValue(sourceKey, out var mtime)
                ? mtime
                : DateTimeOffset.UtcNow;

            EnsureAncestors(destKey, view);
            OnMutationApplied();
            return default;
        }

        if (IsDirectory(sourceKey, view))
        {
            var descendants = CollectDescendantPaths(sourceKey, view);
            if (descendants.Count > 0)
            {
                throw new NotSupportedException(
                    "Copying non-empty directories is not supported by ZipArchiveFileSystem.");
            }

            if (view.Files.ContainsKey(destKey) || IsDirectory(destKey, view))
            {
                throw new VfsNotDirectoryException($"Destination already exists: '{destination}'.");
            }

            EnsureAncestors(destKey, view);
            view.Directories.Add(destKey);
            view.DirectoryTimes[destKey] = DateTimeOffset.UtcNow;
            OnMutationApplied();
            return default;
        }

        throw new VfsNotFoundException($"Source not found: '{source}'.");
    }

    /// <inheritdoc />
    public ValueTask CreateSymbolicLinkAsync(
        FsPath path,
        RelativePath target,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        throw new NotSupportedException("ZIP archives do not support symbolic links.");
    }

    /// <inheritdoc />
    public async ValueTask ReplaceEntryAsync(
        FsPath path,
        Stream newContent,
        ContainerPatchOptions options,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(newContent);
        ArgumentNullException.ThrowIfNull(options);
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();
        ThrowIfNotWritable();

        if (!newContent.CanRead)
        {
            throw new ArgumentException("New content stream must be readable.", nameof(newContent));
        }

        var key = path.ToString();
        var view = GetView();

        if (!view.Files.ContainsKey(key))
        {
            if (IsDirectory(key, view))
            {
                throw new VfsNotDirectoryException($"'{path}' is a directory.");
            }

            throw new VfsNotFoundException($"Entry not found: '{path}'.");
        }

        await using var buffer = new MemoryStream();
        await newContent.CopyToAsync(buffer, ct).ConfigureAwait(false);

        view.Files[key] = buffer.ToArray();
        view.FileTimes[key] = DateTimeOffset.UtcNow;

        OnMutationApplied();
    }

    /// <inheritdoc />
    public IContainerWriteSession BeginWriteSession()
    {
        ThrowIfDisposed();
        ThrowIfNotWritable();

        if (_activeSession is not null)
        {
            throw new InvalidOperationException("A write session is already active on this file system.");
        }

        var session = new WriteSessionState(this);
        _activeSession = session;
        return session;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return default;
        }

        _disposed = true;
        _activeSession?.RollbackInternal();
        _activeSession = null;

        if (!_leaveOpen)
        {
            _source.Dispose();
        }

        return default;
    }

    private void LoadArchive()
    {
        using var archive = new ZipArchive(_source, ZipArchiveMode.Read, leaveOpen: true);

        foreach (var entry in archive.Entries)
        {
            _rawEntryNames.Add(entry.FullName);

            var normalized = NormalizeEntryName(entry.FullName);
            if (normalized is null)
            {
                continue;
            }

            if (normalized.EndsWith('/'))
            {
                var dirKey = "/" + normalized.TrimEnd('/');
                _directoryTimes[dirKey] = entry.LastWriteTime;
            }
            else
            {
                var fileKey = "/" + normalized;
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                _files[fileKey] = buffer.ToArray();
                _fileTimes[fileKey] = entry.LastWriteTime;

                // Ensure implicit parent directories are registered.
                EnsureAncestorsInDictionary(fileKey, _directoryTimes);
            }
        }
    }

    private static void EnsureAncestorsInDictionary(
        string key,
        Dictionary<string, DateTimeOffset> directoryTimes)
    {
        var current = GetDirectoryName(key);
        var now = DateTimeOffset.UtcNow;

        while (current is not null && current != "/")
        {
            if (!directoryTimes.ContainsKey(current))
            {
                directoryTimes[current] = now;
            }

            current = GetDirectoryName(current);
        }
    }

    private void Repack()
    {
        // Build the new archive in memory, then swap into the source stream.
        _source.Position = 0;
        _source.SetLength(0);

        using (var archive = new ZipArchive(_source, ZipArchiveMode.Create, leaveOpen: true))
        {
            var written = new HashSet<string>(StringComparer.Ordinal);

            // Write explicit directory entries first, so their timestamps survive.
            foreach (var (dirKey, mtime) in _directoryTimes)
            {
                if (dirKey == "/")
                {
                    continue;
                }

                var name = dirKey.TrimStart('/') + "/";
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = mtime;
                written.Add(dirKey);
            }

            foreach (var (fileKey, content) in _files)
            {
                var name = fileKey.TrimStart('/');
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = _fileTimes.TryGetValue(fileKey, out var mtime)
                    ? mtime
                    : _constructedAt;

                using var stream = entry.Open();
                stream.Write(content, 0, content.Length);
            }
        }

        _source.Flush();
    }

    private void CommitWrite(WriteView view, string key, byte[] content, DateTimeOffset mtime)
    {
        view.Files[key] = content;
        view.FileTimes[key] = mtime;

        OnMutationApplied();
    }

    private void OnMutationApplied()
    {
        // In a session, nothing goes to the source stream; the session commits later.
        if (_activeSession is not null)
        {
            return;
        }

        Repack();
    }

    private static void EnsureAncestors(string key, WriteView view)
    {
        var current = GetDirectoryName(key);
        while (current is not null && current != "/")
        {
            if (view.Directories.Add(current))
            {
                view.DirectoryTimes[current] = DateTimeOffset.UtcNow;
            }

            current = GetDirectoryName(current);
        }
    }

    private static bool IsDirectory(string key, WriteView view)
        => key == "/" || view.Directories.Contains(key);

    private static List<(string Key, bool IsDirectory)> CollectChildren(string parentKey, WriteView view)
    {
        var result = new List<(string, bool)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var prefix = parentKey == "/" ? "/" : parentKey + "/";

        foreach (var fileKey in view.Files.Keys)
        {
            if (!fileKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var remainder = fileKey[prefix.Length..];
            var slash = remainder.IndexOf('/');
            var childName = slash < 0 ? remainder : remainder[..slash];

            if (slash >= 0)
            {
                var childKey = prefix + childName;
                if (seen.Add(childKey))
                {
                    result.Add((childKey, true));
                }
            }
            else
            {
                if (seen.Add(fileKey))
                {
                    result.Add((fileKey, false));
                }
            }
        }

        foreach (var dirKey in view.Directories)
        {
            if (dirKey == parentKey)
            {
                continue;
            }

            if (!dirKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var remainder = dirKey[prefix.Length..];
            if (remainder.Contains('/'))
            {
                var childName = remainder[..remainder.IndexOf('/')];
                var childKey = prefix + childName;
                if (seen.Add(childKey))
                {
                    result.Add((childKey, true));
                }
            }
            else
            {
                if (seen.Add(dirKey))
                {
                    result.Add((dirKey, true));
                }
            }
        }

        result.Sort(static (a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return result;
    }

    private static List<string> CollectDescendantPaths(string dirKey, WriteView view)
    {
        var result = new List<string>();
        var prefix = dirKey + "/";

        foreach (var fileKey in view.Files.Keys)
        {
            if (fileKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                result.Add(fileKey);
            }
        }

        foreach (var subDirKey in view.Directories)
        {
            if (subDirKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                result.Add(subDirKey);
            }
        }

        return result;
    }

    private FileEntry MaterializeFile(string key, byte[] content, WriteView view)
    {
        var mtime = view.FileTimes.TryGetValue(key, out var t) ? t : _constructedAt;
        var crc = ComputeCrc32(content);

        return new FileEntry(FsPath.Parse(key))
        {
            Size = content.Length,
            LastModified = mtime,
            Created = mtime,
            Accessed = mtime,
            Attributes = FileAttributes.Normal,
            ContentHash = $"crc32:{crc:x8}",
        };
    }

    private DirectoryEntry MaterializeDirectory(string key, WriteView view)
    {
        var mtime = view.DirectoryTimes.TryGetValue(key, out var t) ? t : _constructedAt;

        return new DirectoryEntry(FsPath.Parse(key))
        {
            Size = 0,
            LastModified = mtime,
            Created = mtime,
            Accessed = mtime,
            Attributes = FileAttributes.Directory,
        };
    }

    private static uint ComputeCrc32(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
            }
        }

        return ~crc;
    }

    private static FileStream OpenFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static string? GetDirectoryName(string key)
    {
        var idx = key.LastIndexOf('/');
        if (idx < 0)
        {
            return null;
        }

        return idx == 0 ? "/" : key[..idx];
    }

    /// <summary>
    /// Normalizes a ZIP entry name into an internal path form, or returns
    /// <see langword="null"/> when the name cannot be represented as a valid
    /// <see cref="FsPath"/>.
    /// </summary>
    /// <param name="name">The raw entry name from the ZIP central directory.</param>
    /// <returns>The cleaned path with a trailing slash for directory entries, or <see langword="null"/> when rejected.</returns>
    internal static string? NormalizeEntryName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var normalized = name.Replace('\\', '/');
        var isDirectory = normalized.EndsWith('/');
        var trimmed = isDirectory ? normalized.TrimEnd('/') : normalized;

        if (trimmed.Length == 0 || trimmed[0] == '/')
        {
            return null;
        }

        var segments = trimmed.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                return null;
            }

            foreach (var c in segment)
            {
                if (c < 0x20 || c == 0x7F)
                {
                    return null;
                }
            }
        }

        var result = string.Join('/', segments);
        return isDirectory ? result + "/" : result;
    }

    private static void ValidatePath(FsPath path)
    {
        if (path.IsDefault)
        {
            throw new ArgumentException("Path must be initialized.", nameof(path));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void ThrowIfNotWritable()
    {
        if (!_writable)
        {
            throw new VfsReadOnlyException(
                "The underlying stream is not writable and seekable; this ZipArchiveFileSystem is read-only.");
        }
    }

    /// <summary>
    /// Returns the view of the archive the caller should see. When a write session
    /// is active, the view is the session's overlay; otherwise it is the base state.
    /// </summary>
    private WriteView GetView() => _activeSession?.View ?? new WriteView(_files, _fileTimes, _directoryTimes);

    private void OnStreamOpened() => _activeSession?.OnStreamOpened();

    private void OnStreamClosed() => _activeSession?.OnStreamClosed();

    /// <summary>Mutable view of the archive state used by reads, writes, and session overlays.</summary>
    private sealed class WriteView
    {
        public WriteView(
            Dictionary<string, byte[]> files,
            Dictionary<string, DateTimeOffset> fileTimes,
            Dictionary<string, DateTimeOffset> directoryTimes)
        {
            Files = files;
            FileTimes = fileTimes;
            DirectoryTimes = directoryTimes;
            Directories = new HashSet<string>(StringComparer.Ordinal) { "/" };
            foreach (var key in directoryTimes.Keys)
            {
                Directories.Add(key);
            }
        }

        public Dictionary<string, byte[]> Files { get; }

        public Dictionary<string, DateTimeOffset> FileTimes { get; }

        public Dictionary<string, DateTimeOffset> DirectoryTimes { get; }

        public HashSet<string> Directories { get; }
    }

    /// <summary>
    /// Session state: a copy of the base dictionaries, plus tracking of open write
    /// streams. Reads and writes route through <see cref="View"/> while the session
    /// is active. Commit merges the overlay into the base and triggers a repack.
    /// </summary>
    private sealed class WriteSessionState : IContainerWriteSession
    {
        private readonly ZipArchiveFileSystem _owner;
        private readonly Dictionary<string, byte[]> _overlayFiles;
        private readonly Dictionary<string, DateTimeOffset> _overlayFileTimes;
        private readonly Dictionary<string, DateTimeOffset> _overlayDirTimes;
        private int _openStreams;
        private bool _finished;

        public WriteSessionState(ZipArchiveFileSystem owner)
        {
            _owner = owner;

            _overlayFiles = new Dictionary<string, byte[]>(owner._files, StringComparer.Ordinal);
            _overlayFileTimes = new Dictionary<string, DateTimeOffset>(owner._fileTimes, StringComparer.Ordinal);
            _overlayDirTimes = new Dictionary<string, DateTimeOffset>(owner._directoryTimes, StringComparer.Ordinal);

            View = new WriteView(_overlayFiles, _overlayFileTimes, _overlayDirTimes);
        }

        public WriteView View { get; }

        public void OnStreamOpened() => _openStreams++;

        public void OnStreamClosed() => _openStreams--;

        public ValueTask CommitAsync(CancellationToken ct = default)
        {
            ThrowIfFinished();
            ct.ThrowIfCancellationRequested();

            if (_openStreams > 0)
            {
                throw new InvalidOperationException(
                    $"Cannot commit a write session with {_openStreams} open write stream(s). " +
                    "Close all streams before committing.");
            }

            // Merge overlay into base.
            _owner._files.Clear();
            foreach (var (k, v) in _overlayFiles)
            {
                _owner._files[k] = v;
            }

            _owner._fileTimes.Clear();
            foreach (var (k, v) in _overlayFileTimes)
            {
                _owner._fileTimes[k] = v;
            }

            _owner._directoryTimes.Clear();
            foreach (var (k, v) in _overlayDirTimes)
            {
                _owner._directoryTimes[k] = v;
            }

            _finished = true;
            _owner._activeSession = null;
            _owner.Repack();

            return default;
        }

        public void Rollback()
        {
            ThrowIfFinished();
            RollbackInternal();
        }

        public void RollbackInternal()
        {
            _finished = true;
            _owner._activeSession = null;
        }

        public ValueTask DisposeAsync()
        {
            if (!_finished)
            {
                RollbackInternal();
            }

            return default;
        }

        private void ThrowIfFinished()
        {
            if (_finished)
            {
                throw new InvalidOperationException("The write session has already been committed or rolled back.");
            }
        }
    }

    /// <summary>
    /// A write stream that buffers content in memory and invokes a commit callback on
    /// dispose. Tracks open/close for session commit validation.
    /// </summary>
    private sealed class PendingWriteStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly Action<byte[]> _commit;
        private readonly Action _onOpened;
        private readonly Action _onClosed;
        private bool _committed;

        public PendingWriteStream(MemoryStream inner, Action<byte[]> commit, Action onOpened, Action onClosed)
        {
            _inner = inner;
            _commit = commit;
            _onOpened = onOpened;
            _onClosed = onClosed;
            _onOpened();
        }

        public override bool CanRead => false;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => true;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException("This stream is write-only.");

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_committed)
            {
                _committed = true;
                var bytes = _inner.ToArray();
                _inner.Dispose();
                try
                {
                    _commit(bytes);
                }
                finally
                {
                    _onClosed();
                }
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            Dispose(true);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}