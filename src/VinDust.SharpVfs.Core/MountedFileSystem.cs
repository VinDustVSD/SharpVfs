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
using VinDust.SharpVfs.Core.Internal;

namespace VinDust.SharpVfs.Core;

/// <summary>
/// A decorator that adds mount capability to an arbitrary <see cref="IFileSystem"/>.
/// </summary>
/// <remarks>
/// <para>
/// Mount points are stored in an internal <see cref="MountTable"/>. Every operation is
/// dispatched one level at a time: when the requested path falls under a mount point,
/// the call is forwarded to the mounted target with the remaining path; otherwise it
/// is delegated to the wrapped file system. Nested mounts recurse naturally, because
/// the target may itself be a <see cref="MountedFileSystem"/>.
/// </para>
/// <para>
/// Under <see cref="MountOverlapBehavior.Replace"/>, the target fully shadows the base
/// under the mount point. Under <see cref="MountOverlapBehavior.Overlay"/>, read
/// operations fall back to the base when the target has no entry, and directory
/// listings are merged with the target winning on conflicts. Writes always go to the
/// target.
/// </para>
/// </remarks>
public sealed class MountedFileSystem : IMountableFileSystem
{
    private readonly IFileSystem _inner;
    private readonly MountTable _mounts;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="MountedFileSystem"/> class.</summary>
    /// <param name="inner">The file system to wrap.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is <see langword="null"/>.</exception>
    public MountedFileSystem(IFileSystem inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        _mounts = new MountTable(this);
    }

    /// <inheritdoc />
    public FileSystemCapabilities Capabilities => _inner.Capabilities | FileSystemCapabilities.Read;

    /// <inheritdoc />
    public event EventHandler<MountTableChangedEventArgs>? MountTableChanged
    {
        add => _mounts.Changed += value;
        remove => _mounts.Changed -= value;
    }

    /// <inheritdoc />
    public ValueTask<MountPoint> MountAsync(FsPath path, IFileSystem target, MountOptions options, CancellationToken ct = default)
        => _mounts.MountAsync(path, target, options, ct);

    /// <inheritdoc />
    public ValueTask UnmountAsync(FsPath path, CancellationToken ct = default)
        => _mounts.UnmountAsync(path, ct);

    /// <inheritdoc />
    public IReadOnlyCollection<MountPoint> GetMounts() => _mounts.GetMounts();

    /// <inheritdoc />
    public MountPoint? TryResolveMount(FsPath path, out FsPath remaining)
        => _mounts.TryResolveMount(path, out remaining);

    /// <inheritdoc />
    public async ValueTask<bool> ExistsAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        using var _ = MountScope.Enter(this);

        var mount = _mounts.TryResolveMount(path, out var remaining);
        if (mount is null)
        {
            return await _inner.ExistsAsync(path, ct).ConfigureAwait(false);
        }

        if (await mount.Target.ExistsAsync(remaining, ct).ConfigureAwait(false))
        {
            return true;
        }

        return mount.Options.OverlapBehavior == MountOverlapBehavior.Overlay
            && await _inner.ExistsAsync(path, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<FileSystemEntry?> GetEntryAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        using var _ = MountScope.Enter(this);

        var mount = _mounts.TryResolveMount(path, out var remaining);
        if (mount is null)
        {
            return await _inner.GetEntryAsync(path, ct).ConfigureAwait(false);
        }

        var entry = await mount.Target.GetEntryAsync(remaining, ct).ConfigureAwait(false);
        if (entry is not null)
        {
            return entry;
        }

        return mount.Options.OverlapBehavior == MountOverlapBehavior.Overlay
            ? await _inner.GetEntryAsync(path, ct).ConfigureAwait(false)
            : null;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<FileSystemEntry> EnumerateAsync(FsPath path, CancellationToken ct = default)
        => EnumerateCoreAsync(path, ct);

    /// <inheritdoc />
    public async ValueTask<Stream> OpenReadAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        using var _ = MountScope.Enter(this);

        var mount = _mounts.TryResolveMount(path, out var remaining);
        if (mount is null)
        {
            return await _inner.OpenReadAsync(path, ct).ConfigureAwait(false);
        }

        try
        {
            return await mount.Target.OpenReadAsync(remaining, ct).ConfigureAwait(false);
        }
        catch (VfsNotFoundException) when (mount.Options.OverlapBehavior == MountOverlapBehavior.Overlay)
        {
            return await _inner.OpenReadAsync(path, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask<Stream> OpenWriteAsync(
        FsPath path,
        FileWriteMode mode = FileWriteMode.Create,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        using var _ = MountScope.Enter(this);

        var mount = _mounts.TryResolveMount(path, out var remaining);
        if (mount is null)
        {
            return await _inner.OpenWriteAsync(path, mode, ct).ConfigureAwait(false);
        }

        if (mount.Options.ReadOnly)
        {
            throw new VfsReadOnlyException($"Path '{path}' resolves to a read-only mount point.");
        }

        return await mount.Target.OpenWriteAsync(remaining, mode, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask CreateDirectoryAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        using var _ = MountScope.Enter(this);

        var mount = _mounts.TryResolveMount(path, out var remaining);
        if (mount is null)
        {
            await _inner.CreateDirectoryAsync(path, ct).ConfigureAwait(false);
            return;
        }

        if (mount.Options.ReadOnly)
        {
            throw new VfsReadOnlyException($"Path '{path}' resolves to a read-only mount point.");
        }

        await mount.Target.CreateDirectoryAsync(remaining, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(FsPath path, bool recursive = false, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        using var _ = MountScope.Enter(this);

        var mount = _mounts.TryResolveMount(path, out var remaining);
        if (mount is null)
        {
            await _inner.DeleteAsync(path, recursive, ct).ConfigureAwait(false);
            return;
        }

        if (mount.Options.ReadOnly)
        {
            throw new VfsReadOnlyException($"Path '{path}' resolves to a read-only mount point.");
        }

        if (mount.Options.OverlapBehavior == MountOverlapBehavior.Overlay)
        {
            if (await mount.Target.ExistsAsync(remaining, ct).ConfigureAwait(false))
            {
                await mount.Target.DeleteAsync(remaining, recursive, ct).ConfigureAwait(false);
            }
            else
            {
                await _inner.DeleteAsync(path, recursive, ct).ConfigureAwait(false);
            }

            return;
        }

        await mount.Target.DeleteAsync(remaining, recursive, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask MoveAsync(FsPath source, FsPath destination, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        using var _ = MountScope.Enter(this);

        var sourceMount = _mounts.TryResolveMount(source, out var sourceRemaining);
        var destinationMount = _mounts.TryResolveMount(destination, out var destinationRemaining);

        if ((sourceMount?.Options.ReadOnly ?? false) || (destinationMount?.Options.ReadOnly ?? false))
        {
            throw new VfsReadOnlyException(
                $"Move from '{source}' to '{destination}' touches a read-only mount point.");
        }

        var sourceFs = sourceMount?.Target ?? _inner;
        var destinationFs = destinationMount?.Target ?? _inner;

        if (!ReferenceEquals(sourceFs, destinationFs))
        {
            throw new NotSupportedException(
                "Cross-file-system move is not supported by MountedFileSystem. " +
                "Use a higher-level composition such as UnionFileSystem to transfer across file systems.");
        }

        var sourcePath = sourceMount is null ? source : sourceRemaining;
        var destinationPath = destinationMount is null ? destination : destinationRemaining;

        await sourceFs.MoveAsync(sourcePath, destinationPath, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask CopyAsync(FsPath source, FsPath destination, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        using var _ = MountScope.Enter(this);

        var sourceMount = _mounts.TryResolveMount(source, out var sourceRemaining);
        var destinationMount = _mounts.TryResolveMount(destination, out var destinationRemaining);

        if (destinationMount?.Options.ReadOnly ?? false)
        {
            throw new VfsReadOnlyException(
                $"Copy to '{destination}' targets a read-only mount point.");
        }

        var sourceFs = sourceMount?.Target ?? _inner;
        var destinationFs = destinationMount?.Target ?? _inner;

        if (!ReferenceEquals(sourceFs, destinationFs))
        {
            throw new NotSupportedException(
                "Cross-file-system copy is not supported by MountedFileSystem. " +
                "Use a higher-level composition such as UnionFileSystem to transfer across file systems.");
        }

        var sourcePath = sourceMount is null ? source : sourceRemaining;
        var destinationPath = destinationMount is null ? destination : destinationRemaining;

        await sourceFs.CopyAsync(sourcePath, destinationPath, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask CreateSymbolicLinkAsync(FsPath path, RelativePath target, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        using var _ = MountScope.Enter(this);

        var mount = _mounts.TryResolveMount(path, out var remaining);
        if (mount is null)
        {
            await _inner.CreateSymbolicLinkAsync(path, target, ct).ConfigureAwait(false);
            return;
        }

        if (mount.Options.ReadOnly)
        {
            throw new VfsReadOnlyException($"Path '{path}' resolves to a read-only mount point.");
        }

        await mount.Target.CreateSymbolicLinkAsync(remaining, target, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var mount in _mounts.GetMounts())
        {
            await mount.Target.DisposeAsync().ConfigureAwait(false);
        }

        await _inner.DisposeAsync().ConfigureAwait(false);
    }

    private async IAsyncEnumerable<FileSystemEntry> EnumerateCoreAsync(
        FsPath path,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ThrowIfDisposed();
        using var _ = MountScope.Enter(this);

        var mount = _mounts.TryResolveMount(path, out var remaining);

        if (mount is null)
        {
            await foreach (var entry in _inner.EnumerateAsync(path, ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                yield return entry;
            }

            yield break;
        }

        if (mount.Options.OverlapBehavior == MountOverlapBehavior.Replace)
        {
            await foreach (var entry in mount.Target.EnumerateAsync(remaining, ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                yield return entry;
            }

            yield break;
        }

        // Overlay: check whether the target has a directory here. If it does, merge.
        var targetEntry = await mount.Target.GetEntryAsync(remaining, ct).ConfigureAwait(false);

        if (targetEntry is DirectoryEntry)
        {
            await foreach (var entry in DirectoryMerger.MergeAsync(
                mount.Target.EnumerateAsync(remaining, ct),
                _inner.EnumerateAsync(path, ct),
                ct).ConfigureAwait(false))
            {
                yield return entry;
            }

            yield break;
        }

        if (targetEntry is not null)
        {
            // Non-directory at the target: propagate its enumeration behavior (likely a throw).
            await foreach (var entry in mount.Target.EnumerateAsync(remaining, ct)
                .WithCancellation(ct)
                .ConfigureAwait(false))
            {
                yield return entry;
            }

            yield break;
        }

        // Target has nothing here: fall through entirely to the base.
        await foreach (var entry in _inner.EnumerateAsync(path, ct)
            .WithCancellation(ct)
            .ConfigureAwait(false))
        {
            yield return entry;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}