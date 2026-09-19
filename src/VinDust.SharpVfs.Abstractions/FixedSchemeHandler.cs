namespace VinDust.SharpVfs.Abstractions;

/// <summary>
/// An <see cref="ISchemeHandler"/> that always returns the same file system instance.
/// </summary>
public sealed class FixedSchemeHandler : ISchemeHandler
{
    private readonly IFileSystem _fileSystem;

    /// <summary>Initializes a new instance of the <see cref="FixedSchemeHandler"/> class.</summary>
    /// <param name="fileSystem">The file system to return on every call.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    public FixedSchemeHandler(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public ValueTask<IFileSystem> CreateAsync(CancellationToken ct = default)
        => new(_fileSystem);
}
