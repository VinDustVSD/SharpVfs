using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core;

/// <summary>
/// Maps schemes to decorated, mountable file system instances, caching the result
/// so that a given scheme yields a single live instance for the lifetime of the
/// owning <see cref="VfsRoot"/>.
/// </summary>
/// <remarks>
/// <para>
/// Not an <see cref="IFileSystem"/>: the dispatcher operates at the scheme level,
/// before any <see cref="FsPath"/> exists. It is used exclusively by
/// <see cref="VfsRoot"/> to translate the scheme portion of a <see cref="VfsUri"/>
/// into a concrete file system.
/// </para>
/// <para>
/// When a handler returns a non-mountable file system, the dispatcher applies the
/// configured <see cref="IMountDecoratorFactory"/> so that every cached instance
/// implements <see cref="IMountableFileSystem"/>. This is what makes
/// <c>VfsRoot.MountAsync</c> uniformly available across schemes.
/// </para>
/// <para>
/// Handlers are invoked at most once per scheme under normal operation, even in the
/// presence of concurrent first-access. Cached instances are disposed when the
/// dispatcher is disposed.
/// </para>
/// </remarks>
internal sealed class SchemeDispatcher : IAsyncDisposable
{
    private readonly ISchemeRegistry _registry;
    private readonly IMountDecoratorFactory _decoratorFactory;
    private readonly Dictionary<VfsScheme, IMountableFileSystem> _cache = new();
    private readonly SemaphoreSlim _creationGate = new(1, 1);
    private bool _disposed;

    public SchemeDispatcher(ISchemeRegistry registry, IMountDecoratorFactory decoratorFactory)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(decoratorFactory);

        _registry = registry;
        _decoratorFactory = decoratorFactory;
    }

    public async ValueTask<IMountableFileSystem> GetFileSystemAsync(VfsScheme scheme, CancellationToken ct)
    {
        if (scheme.IsDefault)
        {
            throw new ArgumentException("Scheme must be initialized.", nameof(scheme));
        }

        ThrowIfDisposed();

        lock (_cache)
        {
            if (_cache.TryGetValue(scheme, out var cached))
            {
                return cached;
            }
        }

        await _creationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            lock (_cache)
            {
                if (_cache.TryGetValue(scheme, out var cached))
                {
                    return cached;
                }
            }

            var handler = _registry.GetHandler(scheme)
                ?? throw new VfsSchemeNotFoundException($"Scheme '{scheme}' is not registered.");

            var raw = await handler.CreateAsync(ct).ConfigureAwait(false);
            var decorated = _decoratorFactory.Decorate(raw);

            lock (_cache)
            {
                _cache[scheme] = decorated;
            }

            return decorated;
        }
        finally
        {
            _creationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<IMountableFileSystem> toDispose;

        await _creationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            lock (_cache)
            {
                toDispose = new List<IMountableFileSystem>(_cache.Values);
                _cache.Clear();
            }
        }
        finally
        {
            _creationGate.Release();
        }

        foreach (var fs in toDispose)
        {
            await fs.DisposeAsync().ConfigureAwait(false);
        }

        _creationGate.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}