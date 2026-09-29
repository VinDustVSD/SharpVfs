using System.Runtime.CompilerServices;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.FileSystems.Physical;

/// <summary>
/// A file system backed by a directory on the local disk.
/// </summary>
/// <remarks>
/// <para>
/// The file system is a view onto a subtree of the real file system. <see cref="FsPath.Root"/>
/// corresponds to the configured root directory. Paths outside that subtree are not
/// reachable through this file system: segments containing <c>..</c> are rejected by
/// <see cref="FsPath"/> itself, and the mapping to real paths never leaves the root.
/// </para>
/// <para>
/// Symbolic links are surfaced as <see cref="SymbolicLink"/> entries. Read and write
/// operations follow links transparently, matching the behavior of the BCL
/// <see cref="System.IO.File"/> APIs. Following a link that escapes the root is
/// permitted: sandboxing is not the responsibility of this type.
/// </para>
/// <para>
/// All operations are synchronous under the hood; asynchronous signatures exist to
/// satisfy <see cref="IFileSystem"/> and to preserve cancellation.
/// </para>
/// </remarks>
public sealed class PhysicalFileSystem : IFileSystem
{
    private readonly PhysicalFileSystemOptions _options;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="PhysicalFileSystem"/> class over the specified root.</summary>
    /// <param name="rootPath">The absolute path of the root directory.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rootPath"/> is null, empty, or not absolute.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the root does not exist.</exception>
    public PhysicalFileSystem(string rootPath)
        : this(rootPath, PhysicalFileSystemOptions.Default)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PhysicalFileSystem"/> class over the specified root.</summary>
    /// <param name="rootPath">The absolute path of the root directory.</param>
    /// <param name="options">The behavior options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rootPath"/> is null, empty, or not absolute.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the root does not exist and <see cref="PhysicalFileSystemOptions.CreateRootIfMissing"/> is disabled.</exception>
    public PhysicalFileSystem(string rootPath, PhysicalFileSystemOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        ArgumentNullException.ThrowIfNull(options);

        if (!Path.IsPathRooted(rootPath))
        {
            throw new ArgumentException("Root path must be absolute.", nameof(rootPath));
        }

        _options = options;
        RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

        if (!Directory.Exists(RootPath))
        {
            if (_options.CreateRootIfMissing)
            {
                Directory.CreateDirectory(RootPath);
            }
            else
            {
                throw new DirectoryNotFoundException($"Root directory does not exist: '{RootPath}'.");
            }
        }
    }

    /// <summary>Gets the absolute path of the root directory on the local disk.</summary>
    public string RootPath { get; }

    /// <inheritdoc />
    public FileSystemCapabilities Capabilities =>
        FileSystemCapabilities.Read
        | FileSystemCapabilities.Write
        | FileSystemCapabilities.CreateDirectory
        | FileSystemCapabilities.Delete
        | FileSystemCapabilities.Move
        | FileSystemCapabilities.Copy
        | FileSystemCapabilities.Symlinks
        | FileSystemCapabilities.Seekable;

    /// <inheritdoc />
    public ValueTask<bool> ExistsAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();

        var real = ToRealPath(path);
        return new(File.Exists(real) || Directory.Exists(real));
    }

    /// <inheritdoc />
    public ValueTask<FileSystemEntry?> GetEntryAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();

        var real = ToRealPath(path);

        // Symlink detection: FileInfo.LinkTarget works for both file and directory links.
        var info = new FileInfo(real);
        if (info.LinkTarget is not null)
        {
            return new(MaterializeSymlink(path, real, info));
        }

        if (File.Exists(real))
        {
            return new(MaterializeFile(path, real));
        }

        if (Directory.Exists(real))
        {
            return new(MaterializeDirectory(path, real));
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

        var real = ToRealPath(path);
        if (!Directory.Exists(real))
        {
            throw new VfsNotDirectoryException($"Directory not found: '{path}'.");
        }

        foreach (var childReal in Directory.EnumerateFileSystemEntries(real))
        {
            ct.ThrowIfCancellationRequested();

            var childName = Path.GetFileName(childReal);
            var childPath = FsPath.Parse(
                path.IsRoot ? "/" + childName : path + "/" + childName);

            var info = new FileInfo(childReal);
            if (info.LinkTarget is not null)
            {
                yield return MaterializeSymlink(childPath, childReal, info);
            }
            else if (File.Exists(childReal))
            {
                yield return MaterializeFile(childPath, childReal);
            }
            else if (Directory.Exists(childReal))
            {
                yield return MaterializeDirectory(childPath, childReal);
            }

            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public ValueTask<Stream> OpenReadAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();

        var real = ToRealPath(path);
        if (Directory.Exists(real))
        {
            throw new VfsNotDirectoryException($"'{path}' is a directory.");
        }

        if (!File.Exists(real))
        {
            throw new VfsNotFoundException($"File not found: '{path}'.");
        }

        var stream = new FileStream(real, FileMode.Open, FileAccess.Read, FileShare.Read);
        return new(stream);
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

        if (path.IsRoot)
        {
            throw new VfsNotDirectoryException("Cannot open the root directory for writing.");
        }

        var real = ToRealPath(path);
        var parentReal = Path.GetDirectoryName(real)!;

        if (!Directory.Exists(parentReal))
        {
            if (_options.CreateParentOnWrite)
            {
                Directory.CreateDirectory(parentReal);
            }
            else
            {
                throw new VfsNotFoundException($"Parent directory not found for '{path}'.");
            }
        }

        var fileMode = mode switch
        {
            FileWriteMode.Create => FileMode.Create,
            FileWriteMode.CreateExclusive => FileMode.CreateNew,
            FileWriteMode.Append => FileMode.Append,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown write mode."),
        };

        var access = mode == FileWriteMode.Append ? FileAccess.Write : FileAccess.ReadWrite;
        var stream = new FileStream(real, fileMode, access, FileShare.None);
        return new(stream);
    }

    /// <inheritdoc />
    public ValueTask CreateDirectoryAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();

        if (path.IsRoot)
        {
            if (_options.DirectoryExistsBehavior == DirectoryExistsBehavior.Throw && Directory.Exists(RootPath))
            {
                throw new VfsNotDirectoryException("Root directory already exists.");
            }

            return default;
        }

        var real = ToRealPath(path);

        if (Directory.Exists(real))
        {
            if (_options.DirectoryExistsBehavior == DirectoryExistsBehavior.Throw)
            {
                throw new VfsNotDirectoryException($"Directory already exists: '{path}'.");
            }

            return default;
        }

        if (File.Exists(real))
        {
            throw new VfsNotDirectoryException($"'{path}' already exists and is not a directory.");
        }

        var parentReal = Path.GetDirectoryName(real)!;
        if (!Directory.Exists(parentReal))
        {
            if (_options.CreateParentOnWrite)
            {
                Directory.CreateDirectory(real);
            }
            else
            {
                throw new VfsNotFoundException($"Parent directory not found for '{path}'.");
            }
        }
        else
        {
            Directory.CreateDirectory(real);
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(FsPath path, bool recursive = false, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();

        if (path.IsRoot)
        {
            throw new VfsNotDirectoryException("Cannot delete the root directory.");
        }

        var real = ToRealPath(path);

        if (Directory.Exists(real))
        {
            if (!recursive && Directory.EnumerateFileSystemEntries(real).Any())
            {
                throw new VfsNotDirectoryException(
                    $"Directory '{path}' is not empty. Pass recursive: true to delete it.");
            }

            Directory.Delete(real, recursive);
            return default;
        }

        if (File.Exists(real))
        {
            File.Delete(real);
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

        if (source.IsRoot || destination.IsRoot)
        {
            throw new VfsNotDirectoryException("Cannot move or overwrite the root directory.");
        }

        if (source == destination)
        {
            return default;
        }

        var sourceReal = ToRealPath(source);
        var destReal = ToRealPath(destination);

        var sourceParent = Path.GetDirectoryName(sourceReal)!;
        var destParent = Path.GetDirectoryName(destReal)!;
        EnsureParentExists(destParent);

        if (Directory.Exists(sourceReal))
        {
            if (destination.StartsWith(source))
            {
                throw new VfsNotDirectoryException(
                    $"Cannot move '{source}' into its own subtree '{destination}'.");
            }

            if (Directory.Exists(destReal) || File.Exists(destReal))
            {
                throw new VfsNotDirectoryException($"Destination already exists: '{destination}'.");
            }

            Directory.Move(sourceReal, destReal);
            return default;
        }

        if (File.Exists(sourceReal))
        {
            File.Move(sourceReal, destReal, overwrite: true);
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

        if (source.IsRoot || destination.IsRoot)
        {
            throw new VfsNotDirectoryException("Cannot copy or overwrite the root directory.");
        }

        if (source == destination)
        {
            return default;
        }

        var sourceReal = ToRealPath(source);
        var destReal = ToRealPath(destination);

        var destParent = Path.GetDirectoryName(destReal)!;
        EnsureParentExists(destParent);

        if (Directory.Exists(sourceReal))
        {
            if (destination.StartsWith(source))
            {
                throw new VfsNotDirectoryException(
                    $"Cannot copy '{source}' into its own subtree '{destination}'.");
            }

            if (Directory.Exists(destReal) || File.Exists(destReal))
            {
                throw new VfsNotDirectoryException($"Destination already exists: '{destination}'.");
            }

            CopyDirectoryRecursive(sourceReal, destReal);
            return default;
        }

        if (File.Exists(sourceReal))
        {
            File.Copy(sourceReal, destReal, overwrite: true);
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
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();

        if (target.IsDefault)
        {
            throw new ArgumentException("Target must be initialized.", nameof(target));
        }

        if (path.IsRoot)
        {
            throw new VfsNotDirectoryException("Cannot create a symbolic link at the root.");
        }

        var real = ToRealPath(path);
        var parentReal = Path.GetDirectoryName(real)!;
        EnsureParentExists(parentReal);

        if (File.Exists(real) || Directory.Exists(real))
        {
            throw new VfsNotDirectoryException($"'{path}' already exists.");
        }

        var targetString = target.ToString().Replace('/', Path.DirectorySeparatorChar);

        // Try file symlink first; if the target is a directory, fall back to directory symlink.
        try
        {
            File.CreateSymbolicLink(real, targetString);
        }
        catch (IOException)
        {
            Directory.CreateSymbolicLink(real, targetString);
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return default;
    }

    private string ToRealPath(FsPath path)
    {
        if (path.IsRoot)
        {
            return RootPath;
        }

        var segments = new string[path.SegmentCount];
        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = path.Segments[i];
        }

        return Path.Combine(RootPath, Path.Combine(segments));
    }

    private void EnsureParentExists(string parentReal)
    {
        if (Directory.Exists(parentReal))
        {
            return;
        }

        if (_options.CreateParentOnWrite)
        {
            Directory.CreateDirectory(parentReal);
            return;
        }

        throw new VfsNotFoundException($"Parent directory not found: '{parentReal}'.");
    }

    private static FileEntry MaterializeFile(FsPath path, string real)
    {
        var info = new FileInfo(real);
        return new FileEntry(path)
        {
            Size = info.Length,
            LastModified = info.LastWriteTimeUtc,
            Created = info.CreationTimeUtc,
            Accessed = info.LastAccessTimeUtc,
            Attributes = info.Attributes,
        };
    }

    private static DirectoryEntry MaterializeDirectory(FsPath path, string real)
    {
        var info = new DirectoryInfo(real);
        return new DirectoryEntry(path)
        {
            Size = 0,
            LastModified = info.LastWriteTimeUtc,
            Created = info.CreationTimeUtc,
            Accessed = info.LastAccessTimeUtc,
            Attributes = info.Attributes,
        };
    }

    private static SymbolicLink MaterializeSymlink(FsPath path, string real, FileInfo info)
    {
        var linkTarget = info.LinkTarget!;
        RelativePath relTarget;

        if (Path.IsPathRooted(linkTarget))
        {
            var linkDir = Path.GetDirectoryName(real)!;
            var relString = Path.GetRelativePath(linkDir, linkTarget);
            relTarget = ParseRelative(relString);
        }
        else
        {
            relTarget = ParseRelative(linkTarget);
        }

        return new SymbolicLink(path, relTarget)
        {
            LastModified = info.LastWriteTimeUtc,
            Created = info.CreationTimeUtc,
            Accessed = info.LastAccessTimeUtc,
            Attributes = info.Attributes,
        };
    }

    private static RelativePath ParseRelative(string path)
    {
        var normalized = path.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        if (normalized.Length == 0 || normalized == ".")
        {
            return RelativePath.Current;
        }

        return RelativePath.Parse(normalized);
    }

    private static void CopyDirectoryRecursive(string sourceReal, string destReal)
    {
        Directory.CreateDirectory(destReal);

        foreach (var file in Directory.EnumerateFiles(sourceReal))
        {
            var name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(destReal, name));
        }

        foreach (var subdir in Directory.EnumerateDirectories(sourceReal))
        {
            var name = Path.GetFileName(subdir);
            CopyDirectoryRecursive(subdir, Path.Combine(destReal, name));
        }
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
}