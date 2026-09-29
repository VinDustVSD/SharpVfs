namespace VinDust.SharpVfs.Abstractions;

/// <summary>
/// Creates file systems on demand for a registered scheme.
/// </summary>
/// <remarks>
/// Handlers are invoked by the scheme registry when a <c>VfsUri</c> with the
/// corresponding scheme needs to be resolved. Handlers must not depend on the URI's
/// path or query; the scheme alone determines which file system is produced.
/// </remarks>
public interface ISchemeHandler
{
    /// <summary>Creates or returns the file system associated with this handler's scheme.</summary>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The file system instance.</returns>
    ValueTask<IFileSystem> CreateAsync(CancellationToken ct = default);
}
