using System;
using System.Collections.Generic;
using System.Threading;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Exceptions;

namespace VinDust.SharpVfs.Core.Internal;

/// <summary>
/// Tracks the file systems currently being dispatched on the active async flow to
/// detect mount cycles at resolve time.
/// </summary>
/// <remarks>
/// The scope flows through <c>await</c> via <see cref="AsyncLocal{T}"/>, so nested
/// dispatch through arbitrarily deep mount chains observes the same visited set.
/// </remarks>
internal static class MountScope
{
    private static readonly AsyncLocal<HashSet<IFileSystem>?> Current = new();

    /// <summary>Enters the scope for the specified file system.</summary>
    /// <param name="fileSystem">The file system about to be dispatched on.</param>
    /// <returns>A disposable that exits the scope when disposed.</returns>
    /// <exception cref="VfsMountCycleException">Thrown when <paramref name="fileSystem"/> is already in the current scope.</exception>
    public static IDisposable Enter(IFileSystem fileSystem)
    {
        var set = Current.Value;
        var isRoot = false;

        if (set is null)
        {
            set = new HashSet<IFileSystem>(ReferenceEqualityComparer.Instance);
            Current.Value = set;
            isRoot = true;
        }

        if (!set.Add(fileSystem))
        {
            throw new VfsMountCycleException(
                $"Mount cycle detected: '{fileSystem}' is already on the resolution stack.");
        }

        return new Scope(set, fileSystem, isRoot);
    }

    private sealed class Scope : IDisposable
    {
        private readonly HashSet<IFileSystem> _set;
        private readonly IFileSystem _fileSystem;
        private readonly bool _isRoot;
        private bool _disposed;

        public Scope(HashSet<IFileSystem> set, IFileSystem fileSystem, bool isRoot)
        {
            _set = set;
            _fileSystem = fileSystem;
            _isRoot = isRoot;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _set.Remove(_fileSystem);
            if (_isRoot)
            {
                Current.Value = null;
            }
        }
    }
}