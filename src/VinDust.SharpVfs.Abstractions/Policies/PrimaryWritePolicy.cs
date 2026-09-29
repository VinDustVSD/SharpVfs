namespace VinDust.SharpVfs.Abstractions.Policies;

/// <summary>
/// A write policy that always selects the first candidate.
/// </summary>
/// <remarks>
/// Candidates are expected to be ordered from the topmost overlay layer to the
/// bottommost, so this policy implements "top wins on write" semantics.
/// </remarks>
public sealed class PrimaryWritePolicy : IWritePolicy
{
    /// <summary>A shared singleton instance.</summary>
    public static readonly PrimaryWritePolicy Instance = new();

    private PrimaryWritePolicy()
    {
    }

    /// <inheritdoc />
    public ValueTask<IFileSystem> SelectAsync(WriteContext context, CancellationToken ct = default)
        => new(context.Candidates[0]);
}