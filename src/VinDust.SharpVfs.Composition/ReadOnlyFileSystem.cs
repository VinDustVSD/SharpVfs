using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Composition;

/// <summary>
/// A decorator that presents a read-only view of another file system.
/// </summary>
/// <remarks>
/// <para>
/// Read operations are delegated unchanged. Every mutating operation throws
/// <see cref="VfsReadOnlyException"/>. Capabilities drop all write-related flags.
/// </para>
/// <para>
/// This decorator owns the wrapped file system: disposing the read-only view also
/// disposes the inner instance.
/// </para>
/// <para>
/// A read-only view does not implement <see cref="IMountableFileSystem"/>. Mounting
/// and unmounting are mutating operations and are not exposed through this decorator.
/// </para>
/// </remarks>
public sealed class ReadOnlyFileSystem : IFileSystem
{
    private readonly IFileSystem _inner;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ReadOnlyFileSystem"/> class.</summary>
    /// <param name="inner">The file system to wrap.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is <see langword="null"/>.</exception>
    public ReadOnlyFileSystem(IFileSystem inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
    }

    /// <summary>Gets the wrapped file system.</summary>
    public IFileSystem Inner => _inner;

    /// <inheritdoc />
    public FileSystemCapabilities Capabilities =>
        _inner.Capabilities
        & ~(FileSystemCapabilities.Write
          | FileSystemCapabilities.CreateDirectory
          | FileSystemCapabilities.Delete
          | FileSystemCapabilities.Move
          | FileSystemCapabilities.Copy
          | FileSystemCapabilities.Symlinks);

    /// <inheritdoc />
    public ValueTask<bool> ExistsAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _inner.ExistsAsync(path, ct);
    }

    /// <inheritdoc />
    public ValueTask<FileSystemEntry?> GetEntryAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _inner.GetEntryAsync(path, ct);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<FileSystemEntry> EnumerateAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _inner.EnumerateAsync(path, ct);
    }

    /// <inheritdoc />
    public ValueTask<Stream> OpenReadAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return _inner.OpenReadAsync(path, ct);
    }

    /// <inheritdoc />
    public ValueTask<Stream> OpenWriteAsync(
        FsPath path,
        FileWriteMode mode = FileWriteMode.Create,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        throw new VfsReadOnlyException($"File system is read-only; cannot open '{path}' for writing.");
    }

    /// <inheritdoc />
    public ValueTask CreateDirectoryAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        throw new VfsReadOnlyException($"File system is read-only; cannot create directory '{path}'.");
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(FsPath path, bool recursive = false, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        throw new VfsReadOnlyException($"File system is read-only; cannot delete '{path}'.");
    }

    /// <inheritdoc />
    public ValueTask MoveAsync(FsPath source, FsPath destination, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        throw new VfsReadOnlyException($"File system is read-only; cannot move '{source}'.");
    }

    /// <inheritdoc />
    public ValueTask CopyAsync(FsPath source, FsPath destination, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        throw new VfsReadOnlyException($"File system is read-only; cannot copy '{source}'.");
    }

    /// <inheritdoc />
    public ValueTask CreateSymbolicLinkAsync(
        FsPath path,
        RelativePath target,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        throw new VfsReadOnlyException($"File system is read-only; cannot create symbolic link '{path}'.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _inner.DisposeAsync().ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}