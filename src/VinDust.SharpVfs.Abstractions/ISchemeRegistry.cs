using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Abstractions;

/// <summary>Maps schemes to the handlers that produce their file systems.</summary>
public interface ISchemeRegistry
{
    /// <summary>Gets a snapshot of the currently registered schemes.</summary>
    IReadOnlyCollection<VfsScheme> RegisteredSchemes { get; }

    /// <summary>Gets the handler registered for the specified scheme, or <see langword="null"/>.</summary>
    /// <param name="scheme">The scheme to look up.</param>
    /// <returns>The handler, or <see langword="null"/> if the scheme is not registered.</returns>
    ISchemeHandler? GetHandler(VfsScheme scheme);

    /// <summary>Registers a handler for the specified scheme.</summary>
    /// <param name="scheme">The scheme to register.</param>
    /// <param name="handler">The handler.</param>
    /// <exception cref="System.ArgumentException">Thrown when the scheme is already registered.</exception>
    void Register(VfsScheme scheme, ISchemeHandler handler);

    /// <summary>Removes the handler registered for the specified scheme.</summary>
    /// <param name="scheme">The scheme to remove.</param>
    /// <returns><see langword="true"/> if the scheme was registered and has been removed; otherwise <see langword="false"/>.</returns>
    bool Unregister(VfsScheme scheme);
}
