using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.Abstractions.Policies;
using VinDust.SharpVfs.Core;

namespace VinDust.SharpVfs.Composition;

/// <summary>
/// Presents a list of file systems as a single logical file system.
/// </summary>
/// <remarks>
/// <para>
/// Read operations consult layers from top to bottom: the first layer that has the
/// requested entry (or can open the requested stream) wins. Directory listings are
/// merged across all layers with top-priority on name conflicts.
/// </para>
/// <para>
/// Write operations are routed to a single writable layer selected by an
/// <see cref="IWritePolicy"/>. When no policy is configured,
/// <see cref="PrimaryWritePolicy"/> is used, which always picks the topmost writable
/// layer. Layers are considered writable when their
/// <see cref="IFileSystem.Capabilities"/> include <see cref="FileSystemCapabilities.Write"/>.
/// </para>
/// <para>
/// Deletion removes the entry from every writable layer that has it. Entries that
/// exist only in read-only lower layers remain visible after deletion; there is no
/// whiteout mechanism in v1.
/// </para>
/// <para>
/// Moving and copying are supported only when source and destination resolve to the
/// same writable layer. Cross-layer transfers throw <see cref="NotSupportedException"/>.
/// </para>
/// <para>
/// The union owns its layers: disposing the union disposes every layer.
/// </para>
/// </remarks>
public sealed class UnionFileSystem : IFileSystem
{
    private readonly IReadOnlyList<IFileSystem> _layers;
    private readonly IWritePolicy _writePolicy;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="UnionFileSystem"/> class.</summary>
    /// <param name="layers">
    /// The layers, ordered from highest priority (first) to lowest priority (last).
    /// Must contain at least one layer.
    /// </param>
    /// <param name="writePolicy">The write policy. When <see langword="null"/>, <see cref="PrimaryWritePolicy"/> is used.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="layers"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="layers"/> is empty or contains a <see langword="null"/> element.</exception>
    public UnionFileSystem(IReadOnlyList<IFileSystem> layers, IWritePolicy? writePolicy = null)
    {
        ArgumentNullException.ThrowIfNull(layers);

        if (layers.Count == 0)
        {
            throw new ArgumentException("At least one layer is required.", nameof(layers));
        }

        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i] is null)
            {
                throw new ArgumentException($"Layer at index {i} is null.", nameof(layers));
            }
        }

        _layers = layers;
        _writePolicy = writePolicy ?? PrimaryWritePolicy.Instance;
    }

    /// <summary>Gets the layers, ordered from highest priority to lowest.</summary>
    public IReadOnlyList<IFileSystem> Layers => _layers;

    /// <summary>Gets the write policy used by this union.</summary>
    public IWritePolicy WritePolicy => _writePolicy;

    /// <inheritdoc />
    public FileSystemCapabilities Capabilities
    {
        get
        {
            var read = false;
            var write = false;
            var createDirectory = false;
            var delete = false;
            var move = false;
            var copy = false;
            var symlinks = false;
            var seekable = true;

            foreach (var layer in _layers)
            {
                var layerCaps = layer.Capabilities;

                if (layerCaps.HasFlag(FileSystemCapabilities.Read))
                {
                    read = true;
                }

                if (!layerCaps.HasFlag(FileSystemCapabilities.Seekable))
                {
                    seekable = false;
                }

                if (!layerCaps.HasFlag(FileSystemCapabilities.Write))
                {
                    continue;
                }

                write = true;
                createDirectory |= layerCaps.HasFlag(FileSystemCapabilities.CreateDirectory);
                delete |= layerCaps.HasFlag(FileSystemCapabilities.Delete);
                move |= layerCaps.HasFlag(FileSystemCapabilities.Move);
                copy |= layerCaps.HasFlag(FileSystemCapabilities.Copy);
                symlinks |= layerCaps.HasFlag(FileSystemCapabilities.Symlinks);
            }

            var caps = FileSystemCapabilities.None;
            if (read)
                caps |= FileSystemCapabilities.Read;
            if (write)
                caps |= FileSystemCapabilities.Write;
            if (createDirectory)
                caps |= FileSystemCapabilities.CreateDirectory;
            if (delete)
                caps |= FileSystemCapabilities.Delete;
            if (move)
                caps |= FileSystemCapabilities.Move;
            if (copy)
                caps |= FileSystemCapabilities.Copy;
            if (symlinks)
                caps |= FileSystemCapabilities.Symlinks;
            if (seekable)
                caps |= FileSystemCapabilities.Seekable;
            return caps;
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> ExistsAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);

        foreach (var layer in _layers)
        {
            ct.ThrowIfCancellationRequested();
            if (await layer.ExistsAsync(path, ct).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async ValueTask<FileSystemEntry?> GetEntryAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);

        foreach (var layer in _layers)
        {
            ct.ThrowIfCancellationRequested();
            var entry = await layer.GetEntryAsync(path, ct).ConfigureAwait(false);
            if (entry is not null)
            {
                return entry;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<FileSystemEntry> EnumerateAsync(FsPath path, CancellationToken ct = default)
        => EnumerateCoreAsync(path, ct);

    /// <inheritdoc />
    public async ValueTask<Stream> OpenReadAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);

        foreach (var layer in _layers)
        {
            ct.ThrowIfCancellationRequested();

            if (!await layer.ExistsAsync(path, ct).ConfigureAwait(false))
            {
                continue;
            }

            return await layer.OpenReadAsync(path, ct).ConfigureAwait(false);
        }

        throw new VfsNotFoundException($"File not found in any layer: '{path}'.");
    }

    /// <inheritdoc />
    public async ValueTask<Stream> OpenWriteAsync(
        FsPath path,
        FileWriteMode mode = FileWriteMode.Create,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);

        var target = await SelectWriteLayerAsync(path, ct).ConfigureAwait(false);
        return await target.OpenWriteAsync(path, mode, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask CreateDirectoryAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);

        var target = await SelectWriteLayerAsync(path, ct).ConfigureAwait(false);
        await target.CreateDirectoryAsync(path, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(FsPath path, bool recursive = false, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);

        var deletedAny = false;

        foreach (var layer in _layers)
        {
            ct.ThrowIfCancellationRequested();

            if (!layer.Capabilities.HasFlag(FileSystemCapabilities.Write))
            {
                continue;
            }

            if (await layer.ExistsAsync(path, ct).ConfigureAwait(false))
            {
                await layer.DeleteAsync(path, recursive, ct).ConfigureAwait(false);
                deletedAny = true;
            }
        }

        if (!deletedAny)
        {
            // No writable layer had the entry. This is not an error: the entry may
            // exist in a read-only lower layer, where deletion is not possible.
            return;
        }
    }

    /// <inheritdoc />
    public async ValueTask MoveAsync(FsPath source, FsPath destination, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(source);
        ValidatePath(destination);

        if (source == destination)
        {
            return;
        }

        var sourceLayer = await FindWritableLayerContainingAsync(source, ct).ConfigureAwait(false)
            ?? throw new VfsNotFoundException($"Source not found in any writable layer: '{source}'.");

        var destinationLayer = await SelectWriteLayerAsync(destination, ct).ConfigureAwait(false);

        if (!ReferenceEquals(sourceLayer, destinationLayer))
        {
            throw new NotSupportedException(
                "Cross-layer move is not supported by UnionFileSystem. " +
                "Copy via streams and delete explicitly.");
        }

        await sourceLayer.MoveAsync(source, destination, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask CopyAsync(FsPath source, FsPath destination, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(source);
        ValidatePath(destination);

        if (source == destination)
        {
            return;
        }

        // For copy, the source may be in any layer (read-only or writable).
        var sourceLayer = await FindLayerContainingAsync(source, ct).ConfigureAwait(false)
            ?? throw new VfsNotFoundException($"Source not found in any layer: '{source}'.");

        var destinationLayer = await SelectWriteLayerAsync(destination, ct).ConfigureAwait(false);

        if (!ReferenceEquals(sourceLayer, destinationLayer))
        {
            // Cross-layer: fall back to stream-based copy through the union itself.
            await CopyViaStreamsAsync(source, destination, ct).ConfigureAwait(false);
            return;
        }

        await sourceLayer.CopyAsync(source, destination, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask CreateSymbolicLinkAsync(
        FsPath path,
        RelativePath target,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);

        var layer = await SelectWriteLayerAsync(path, ct).ConfigureAwait(false);
        await layer.CreateSymbolicLinkAsync(path, target, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var layer in _layers)
        {
            await layer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async IAsyncEnumerable<FileSystemEntry> EnumerateCoreAsync(
        FsPath path,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ThrowIfDisposed();
        ValidatePath(path);

        // Determine whether the path refers to a directory in any layer. If not,
        // throw, matching single-file-system semantics.
        var probe = await GetEntryAsync(path, ct).ConfigureAwait(false);
        if (probe is null)
        {
            throw new VfsNotFoundException($"Directory not found: '{path}'.");
        }

        if (probe is not DirectoryEntry)
        {
            throw new VfsNotDirectoryException($"'{path}' is not a directory.");
        }

        IAsyncEnumerable<FileSystemEntry>? merged = null;

        foreach (var layer in _layers)
        {
            ct.ThrowIfCancellationRequested();

            var layerEntries = layer.EnumerateAsync(path, ct);
            merged = merged is null
                ? layerEntries
                : DirectoryMerger.MergeAsync(merged, layerEntries, ct);
        }

        // merged is non-null: we have at least one layer.
        await foreach (var entry in merged!.WithCancellation(ct).ConfigureAwait(false))
        {
            yield return entry;
        }
    }

    private async ValueTask<IFileSystem> SelectWriteLayerAsync(FsPath path, CancellationToken ct)
    {
        var candidates = new List<IFileSystem>(_layers.Count);
        foreach (var layer in _layers)
        {
            if (layer.Capabilities.HasFlag(FileSystemCapabilities.Write))
            {
                candidates.Add(layer);
            }
        }

        if (candidates.Count == 0)
        {
            throw new VfsReadOnlyException(
                $"No writable layer is available for '{path}'.");
        }

        var context = new WriteContext(path, candidates);
        return await _writePolicy.SelectAsync(context, ct).ConfigureAwait(false);
    }

    private async ValueTask<IFileSystem?> FindLayerContainingAsync(FsPath path, CancellationToken ct)
    {
        foreach (var layer in _layers)
        {
            ct.ThrowIfCancellationRequested();
            if (await layer.ExistsAsync(path, ct).ConfigureAwait(false))
            {
                return layer;
            }
        }

        return null;
    }

    private async ValueTask<IFileSystem?> FindWritableLayerContainingAsync(FsPath path, CancellationToken ct)
    {
        foreach (var layer in _layers)
        {
            ct.ThrowIfCancellationRequested();

            if (!layer.Capabilities.HasFlag(FileSystemCapabilities.Write))
            {
                continue;
            }

            if (await layer.ExistsAsync(path, ct).ConfigureAwait(false))
            {
                return layer;
            }
        }

        return null;
    }

    private async ValueTask CopyViaStreamsAsync(FsPath source, FsPath destination, CancellationToken ct)
    {
        await using var readStream = await OpenReadAsync(source, ct).ConfigureAwait(false);

        var destinationLayer = await SelectWriteLayerAsync(destination, ct).ConfigureAwait(false);
        await using var writeStream = await destinationLayer
            .OpenWriteAsync(destination, FileWriteMode.Create, ct)
            .ConfigureAwait(false);

        await readStream.CopyToAsync(writeStream, ct).ConfigureAwait(false);
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