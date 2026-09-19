using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core;

/// <summary>
/// The user-facing entry point into a virtual file system tree.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="VfsRoot"/> owns a scheme registry and a set of decorated, mountable
/// file systems — one per registered scheme. All public operations take
/// <see cref="VfsUri"/> values; the scheme portion is dispatched through the registry,
/// and the path portion is handled by the resulting file system, which is responsible
/// for resolving any mount points within it.
/// </para>
/// <para>
/// The same <see cref="VfsRoot"/> is required to reach the same file system instances
/// across calls; instances are cached per scheme and disposed when the root is disposed.
/// </para>
/// </remarks>
public sealed class VfsRoot : IAsyncDisposable
{
    private readonly ISchemeRegistry _registry;
    private readonly IContentHashProvider? _hashProvider;
    private readonly SchemeDispatcher _dispatcher;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="VfsRoot"/> class.</summary>
    /// <param name="registry">The scheme registry. When <see langword="null"/>, a new empty <see cref="DefaultSchemeRegistry"/> is created.</param>
    /// <param name="decoratorFactory">The mount decorator factory. When <see langword="null"/>, <see cref="DefaultMountDecoratorFactory.Instance"/> is used.</param>
    /// <param name="hashProvider">An optional content hash provider used when an entry has no eager hash.</param>
    public VfsRoot(
        ISchemeRegistry? registry = null,
        IMountDecoratorFactory? decoratorFactory = null,
        IContentHashProvider? hashProvider = null)
    {
        _registry = registry ?? new DefaultSchemeRegistry();
        _hashProvider = hashProvider;
        _dispatcher = new SchemeDispatcher(_registry, decoratorFactory ?? DefaultMountDecoratorFactory.Instance);
    }

    /// <summary>Gets the scheme registry used by this root.</summary>
    public ISchemeRegistry Registry => _registry;

    /// <summary>Determines whether an entry exists at the given URI.</summary>
    /// <param name="uri">The URI to check.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns><see langword="true"/> if the entry exists; otherwise <see langword="false"/>.</returns>
    public async ValueTask<bool> ExistsAsync(VfsUri uri, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var fs = await _dispatcher.GetFileSystemAsync(uri.Scheme, ct).ConfigureAwait(false);
        return await fs.ExistsAsync(uri.Path, ct).ConfigureAwait(false);
    }

    /// <summary>Retrieves the entry at the given URI, or <see langword="null"/> if it does not exist.</summary>
    /// <param name="uri">The URI to look up.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>The entry, or <see langword="null"/>.</returns>
    public async ValueTask<VfsEntry?> GetEntryAsync(VfsUri uri, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var fs = await _dispatcher.GetFileSystemAsync(uri.Scheme, ct).ConfigureAwait(false);
        var entry = await fs.GetEntryAsync(uri.Path, ct).ConfigureAwait(false);
        return entry is null ? null : new VfsEntry(uri, entry);
    }

    /// <summary>Enumerates the immediate children of the directory at the given URI.</summary>
    /// <param name="uri">The directory URI.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>An asynchronous sequence of child entries.</returns>
    public async IAsyncEnumerable<VfsEntry> EnumerateAsync(
        VfsUri uri,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var fs = await _dispatcher.GetFileSystemAsync(uri.Scheme, ct).ConfigureAwait(false);

        await foreach (var entry in fs.EnumerateAsync(uri.Path, ct)
            .WithCancellation(ct)
            .ConfigureAwait(false))
        {
            var childName = entry.Path.GetFileName();
            var childUri = childName is null
                ? uri
                : RelativePath.Parse(childName).ResolveAgainst(uri);

            yield return new VfsEntry(childUri, entry);
        }
    }

    /// <summary>Opens the file at the given URI for reading.</summary>
    /// <param name="uri">The file URI.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>A readable stream over the file contents.</returns>
    public async ValueTask<Stream> OpenReadAsync(VfsUri uri, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var fs = await _dispatcher.GetFileSystemAsync(uri.Scheme, ct).ConfigureAwait(false);
        return await fs.OpenReadAsync(uri.Path, ct).ConfigureAwait(false);
    }

    /// <summary>Opens (or creates) the file at the given URI for writing.</summary>
    /// <param name="uri">The file URI.</param>
    /// <param name="mode">Specifies how the file should be opened.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>A writable stream over the file contents.</returns>
    public async ValueTask<Stream> OpenWriteAsync(
        VfsUri uri,
        FileWriteMode mode = FileWriteMode.Create,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var fs = await _dispatcher.GetFileSystemAsync(uri.Scheme, ct).ConfigureAwait(false);
        return await fs.OpenWriteAsync(uri.Path, mode, ct).ConfigureAwait(false);
    }

    /// <summary>Creates a directory at the given URI.</summary>
    /// <param name="uri">The directory URI.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>A task that completes once the directory has been created.</returns>
    public async ValueTask CreateDirectoryAsync(VfsUri uri, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var fs = await _dispatcher.GetFileSystemAsync(uri.Scheme, ct).ConfigureAwait(false);
        await fs.CreateDirectoryAsync(uri.Path, ct).ConfigureAwait(false);
    }

    /// <summary>Deletes the entry at the given URI.</summary>
    /// <param name="uri">The URI to delete.</param>
    /// <param name="recursive">When <see langword="true"/>, deletes a directory and all of its contents.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>A task that completes once the entry has been deleted.</returns>
    public async ValueTask DeleteAsync(VfsUri uri, bool recursive = false, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var fs = await _dispatcher.GetFileSystemAsync(uri.Scheme, ct).ConfigureAwait(false);
        await fs.DeleteAsync(uri.Path, recursive, ct).ConfigureAwait(false);
    }

    /// <summary>Creates a symbolic link at the given URI.</summary>
    /// <param name="uri">The URI at which to create the link.</param>
    /// <param name="target">The target of the link.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>A task that completes once the link has been created.</returns>
    public async ValueTask CreateSymbolicLinkAsync(
        VfsUri uri,
        RelativePath target,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var fs = await _dispatcher.GetFileSystemAsync(uri.Scheme, ct).ConfigureAwait(false);
        await fs.CreateSymbolicLinkAsync(uri.Path, target, ct).ConfigureAwait(false);
    }

    /// <summary>Computes or retrieves the content hash for the entry at the given URI.</summary>
    /// <param name="uri">The entry URI.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>The hash string, or <see langword="null"/> when no hash can be produced.</returns>
    public async ValueTask<string?> GetContentHashAsync(VfsUri uri, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var fs = await _dispatcher.GetFileSystemAsync(uri.Scheme, ct).ConfigureAwait(false);
        var entry = await fs.GetEntryAsync(uri.Path, ct).ConfigureAwait(false);

        if (entry is null)
        {
            return null;
        }

        if (entry.ContentHash is not null)
        {
            return entry.ContentHash;
        }

        if (_hashProvider is null)
        {
            return null;
        }

        await using var stream = await fs.OpenReadAsync(uri.Path, ct).ConfigureAwait(false);
        return await _hashProvider.ComputeAsync(stream, ct).ConfigureAwait(false);
    }

    /// <summary>Mounts a file system at the specified URI.</summary>
    /// <param name="mountPoint">The URI at which to mount.</param>
    /// <param name="target">The file system to mount.</param>
    /// <param name="options">The mount options.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>The created mount point.</returns>
    public async ValueTask<MountPoint> MountAsync(
        VfsUri mountPoint,
        IFileSystem target,
        MountOptions options,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var fs = await _dispatcher.GetFileSystemAsync(mountPoint.Scheme, ct).ConfigureAwait(false);
        return await fs.MountAsync(mountPoint.Path, target, options, ct).ConfigureAwait(false);
    }

    /// <summary>Removes the mount point at the specified URI.</summary>
    /// <param name="mountPoint">The URI of the mount point to remove.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>A task that completes once the mount point has been removed.</returns>
    public async ValueTask UnmountAsync(VfsUri mountPoint, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var fs = await _dispatcher.GetFileSystemAsync(mountPoint.Scheme, ct).ConfigureAwait(false);
        await fs.UnmountAsync(mountPoint.Path, ct).ConfigureAwait(false);
    }

    /// <summary>Returns the mount points directly attached to the file system that owns <paramref name="uri"/>.</summary>
    /// <param name="uri">Any URI within the owning file system.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>A read-only snapshot of mount points.</returns>
    public async ValueTask<IReadOnlyCollection<MountPoint>> GetMountsAsync(
        VfsUri uri,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        var fs = await _dispatcher.GetFileSystemAsync(uri.Scheme, ct).ConfigureAwait(false);
        return fs.GetMounts();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _dispatcher.DisposeAsync().ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}