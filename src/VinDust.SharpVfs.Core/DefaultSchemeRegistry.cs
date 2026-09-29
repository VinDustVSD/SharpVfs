using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core;

/// <summary>
/// The default implementation of <see cref="ISchemeRegistry"/>: a thread-safe map from
/// schemes to handlers.
/// </summary>
public sealed class DefaultSchemeRegistry : ISchemeRegistry
{
    private readonly ConcurrentDictionary<VfsScheme, ISchemeHandler> _handlers = new();

    /// <summary>Initializes a new instance of the <see cref="DefaultSchemeRegistry"/> class.</summary>
    public DefaultSchemeRegistry()
    {
    }

    /// <inheritdoc />
    public IReadOnlyCollection<VfsScheme> RegisteredSchemes
        => new ReadOnlyCollection<VfsScheme>(new List<VfsScheme>(_handlers.Keys));

    /// <inheritdoc />
    public ISchemeHandler? GetHandler(VfsScheme scheme)
    {
        if (scheme.IsDefault)
        {
            return null;
        }

        return _handlers.TryGetValue(scheme, out var handler) ? handler : null;
    }

    /// <inheritdoc />
    public void Register(VfsScheme scheme, ISchemeHandler handler)
    {
        if (scheme.IsDefault)
        {
            throw new ArgumentException("Scheme must be initialized.", nameof(scheme));
        }

        ArgumentNullException.ThrowIfNull(handler);

        if (!_handlers.TryAdd(scheme, handler))
        {
            throw new ArgumentException($"Scheme '{scheme}' is already registered.", nameof(scheme));
        }
    }

    /// <inheritdoc />
    public bool Unregister(VfsScheme scheme)
    {
        if (scheme.IsDefault)
        {
            return false;
        }

        return _handlers.TryRemove(scheme, out _);
    }
}