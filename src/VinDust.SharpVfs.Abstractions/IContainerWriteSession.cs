namespace VinDust.SharpVfs.Abstractions;

/// <summary>
/// A batching scope for container mutations. Changes made while the session is
/// active are accumulated and applied to the container in a single operation on
/// <see cref="CommitAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// The session is single-use: after <see cref="CommitAsync"/> or <see cref="Rollback"/>,
/// or after <see cref="IAsyncDisposable.DisposeAsync"/> without commit, the session
/// is spent and must not be reused.
/// </para>
/// <para>
/// All write streams opened inside the session must be closed before commit.
/// Committing with open streams throws <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// Disposing the session without calling <see cref="CommitAsync"/> performs a rollback.
/// </para>
/// </remarks>
public interface IContainerWriteSession : IAsyncDisposable
{
    /// <summary>Applies all accumulated changes to the underlying container in a single operation.</summary>
    /// <param name="ct">A token to observe.</param>
    /// <returns>A task that completes once the changes have been written.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the session has already been committed, rolled back, or has open write streams.</exception>
    /// <exception cref="System.OperationCanceledException">Thrown when the operation is cancelled.</exception>
    ValueTask CommitAsync(CancellationToken ct = default);

    /// <summary>Discards all accumulated changes.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the session has already been committed or rolled back.</exception>
    void Rollback();
}