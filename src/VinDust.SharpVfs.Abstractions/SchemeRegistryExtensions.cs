namespace VinDust.SharpVfs.Abstractions;

/// <summary>Convenience extensions for <see cref="ISchemeRegistry"/>.</summary>
public static class SchemeRegistryExtensions
{
    /// <summary>Registers a fixed file system instance for the specified scheme.</summary>
    /// <param name="registry">The registry.</param>
    /// <param name="scheme">The scheme to register.</param>
    /// <param name="fileSystem">The file system instance to return on every resolve.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null"/> or a default struct.</exception>
    public static void Register(this ISchemeRegistry registry, Paths.VfsScheme scheme, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(fileSystem);

        registry.Register(scheme, new FixedSchemeHandler(fileSystem));
    }
}
