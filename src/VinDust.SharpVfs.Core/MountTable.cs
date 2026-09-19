using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.Core.Internal;

namespace VinDust.SharpVfs.Core;

/// <summary>
/// Holds the mount points of a single hosting file system and provides mount, unmount,
/// and resolution operations against them.
/// </summary>
/// <remarks>
/// <para>
/// Mount points are stored in a <see cref="PathTrie{TValue}"/> with an RCU-style
/// snapshot: reads are lock-free, writes serialize on an internal lock and publish a
/// new immutable snapshot. A write copies only the nodes along the affected path.
/// </para>
/// <para>
/// A target file system may be mounted at multiple paths. The table maintains a
/// reference count per target and disposes the target when the last mount is removed.
/// </para>
/// <para>
/// This type is thread-safe. All mutations are serialized; reads never block.
/// </para>
/// </remarks>
public sealed class MountTable
{
    private readonly IFileSystem _owner;
    private readonly PathTrie<MountEntry> _trie = new();
    private readonly Lock _writeLock = new();
    private readonly Dictionary<IFileSystem, int> _refCounts = new(ReferenceEqualityComparer.Instance);

    /// <summary>Initializes a new instance of the <see cref="MountTable"/> class.</summary>
    /// <param name="owner">The hosting file system. Used for direct cycle detection.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="owner"/> is <see langword="null"/>.</exception>
    public MountTable(IFileSystem owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        _owner = owner;
    }

    /// <summary>Occurs after the table is modified, with the write lock released.</summary>
    public event EventHandler<MountTableChangedEventArgs>? Changed;

    /// <summary>Gets the number of mount points currently registered.</summary>
    public int Count => _trie.Count;

    /// <summary>Mounts a file system at the specified path.</summary>
    /// <param name="path">The mount point path within the owner file system.</param>
    /// <param name="target">The file system to mount.</param>
    /// <param name="options">The mount options.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>The created mount point.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is the default value.</exception>
    /// <exception cref="VfsMountCycleException">Thrown when the mount would introduce a direct cycle.</exception>
    public ValueTask<MountPoint> MountAsync(FsPath path, IFileSystem target, MountOptions options, CancellationToken ct = default)
    {
        if (path.IsDefault)
        {
            throw new ArgumentException("Path must be initialized.", nameof(path));
        }

        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        ct.ThrowIfCancellationRequested();

        var point = new MountPoint(path, target, options, DateTimeOffset.UtcNow);
        var entry = new MountEntry(point);

        MountTableChangedEventArgs eventArgs;
        IFileSystem? toDispose = null;

        lock (_writeLock)
        {
            if (ReferenceEquals(target, _owner))
            {
                throw new VfsMountCycleException("Cannot mount a file system onto itself.");
            }

            MountEntry? previous = null;
            _trie.TryGet(path, out previous);

            _trie.Set(path, entry);
            IncrementRefCount(target);

            eventArgs = previous is null
                ? new MountTableChangedEventArgs(MountChangeKind.Added, point)
                : new MountTableChangedEventArgs(MountChangeKind.Replaced, point, previous.Point);

            if (previous is not null)
            {
                toDispose = DecrementRefCount(previous.Point.Target);
            }
        }

        if (toDispose is not null)
        {
            // Dispose outside the lock, but the async dispose would be tricky to await
            // here without making MountAsync block on I/O. We schedule disposal and
            // await it before returning.
            return MountWithDisposalAsync(eventArgs, toDispose, ct);
        }

        RaiseChanged(eventArgs);
        return new ValueTask<MountPoint>(point);
    }

    private async ValueTask<MountPoint> MountWithDisposalAsync(
        MountTableChangedEventArgs eventArgs,
        IFileSystem toDispose,
        CancellationToken ct)
    {
        RaiseChanged(eventArgs);
        await toDispose.DisposeAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return eventArgs.Point;
    }

    /// <summary>Removes the mount point at the specified path.</summary>
    /// <param name="path">The mount point path.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>A task that completes once the mount point has been removed and any unreferenced target has been disposed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is the default value.</exception>
    /// <exception cref="VfsNotFoundException">Thrown when no mount point exists at <paramref name="path"/>.</exception>
    public ValueTask UnmountAsync(FsPath path, CancellationToken ct = default)
    {
        if (path.IsDefault)
        {
            throw new ArgumentException("Path must be initialized.", nameof(path));
        }

        ct.ThrowIfCancellationRequested();

        MountTableChangedEventArgs eventArgs;
        IFileSystem? toDispose = null;

        lock (_writeLock)
        {
            if (!_trie.TryGet(path, out var entry) || entry is null)
            {
                throw new VfsNotFoundException($"No mount point exists at '{path}'.");
            }

            _trie.Remove(path);
            toDispose = DecrementRefCount(entry.Point.Target);
            eventArgs = new MountTableChangedEventArgs(MountChangeKind.Removed, entry.Point);
        }

        if (toDispose is null)
        {
            RaiseChanged(eventArgs);
            return default;
        }

        return UnmountWithDisposalAsync(eventArgs, toDispose);
    }

    private async ValueTask UnmountWithDisposalAsync(MountTableChangedEventArgs eventArgs, IFileSystem toDispose)
    {
        RaiseChanged(eventArgs);
        await toDispose.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Returns a snapshot of the current mount points, ordered by path.</summary>
    /// <returns>A read-only collection of mount points.</returns>
    public IReadOnlyCollection<MountPoint> GetMounts()
    {
        var list = new List<MountPoint>();
        foreach (var pair in _trie.Enumerate())
        {
            list.Add(pair.Value.Point);
        }

        list.Sort(static (a, b) => a.Path.CompareTo(b.Path));
        return new ReadOnlyCollection<MountPoint>(list);
    }

    /// <summary>Finds the mount point whose path is the longest matching prefix of <paramref name="path"/>.</summary>
    /// <param name="path">The path to resolve.</param>
    /// <param name="remaining">
    /// When this method returns, contains the remainder of <paramref name="path"/> after the
    /// matched mount point, or <c>default</c> when no mount point matched.
    /// </param>
    /// <returns>The matching mount point, or <see langword="null"/> if no mount point covers <paramref name="path"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is the default value.</exception>
    public MountPoint? TryResolveMount(FsPath path, out FsPath remaining)
    {
        if (path.IsDefault)
        {
            throw new ArgumentException("Path must be initialized.", nameof(path));
        }

        if (_trie.TryResolve(path, out var entry, out remaining) && entry is not null)
        {
            return entry.Point;
        }

        return null;
    }

    private void IncrementRefCount(IFileSystem target)
    {
        _refCounts.TryGetValue(target, out var count);
        _refCounts[target] = count + 1;
    }

    private IFileSystem? DecrementRefCount(IFileSystem target)
    {
        if (!_refCounts.TryGetValue(target, out var count))
        {
            return null;
        }

        if (count <= 1)
        {
            _refCounts.Remove(target);
            return target;
        }

        _refCounts[target] = count - 1;
        return null;
    }

    private void RaiseChanged(MountTableChangedEventArgs eventArgs)
    {
        var handler = Changed;
        handler?.Invoke(this, eventArgs);
    }
}
