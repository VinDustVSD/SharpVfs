namespace VinDust.SharpVfs.Abstractions;

/// <summary>
/// Computes content hashes on demand for entries whose owning file system does not
/// provide a hash eagerly.
/// </summary>
/// <remarks>
/// Providers receive the raw content stream and return an opaque hash string in the
/// canonical form <c>&lt;algorithm&gt;:&lt;hex&gt;</c>, for example <c>sha256:...</c>.
/// They do not manage the stream's lifetime: the caller opens and disposes it.
/// </remarks>
public interface IContentHashProvider
{
    /// <summary>Computes a content hash over the supplied stream.</summary>
    /// <param name="content">A readable stream over the entry content.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The hash string, or <see langword="null"/> if the provider declines to hash the content.</returns>
    ValueTask<string?> ComputeAsync(Stream content, CancellationToken ct = default);
}
