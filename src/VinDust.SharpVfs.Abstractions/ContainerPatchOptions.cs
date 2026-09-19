using System.IO.Compression;

namespace VinDust.SharpVfs.Abstractions;

/// <summary>Options controlling a single container patch operation.</summary>
public sealed class ContainerPatchOptions
{
    /// <summary>Default options: prefer in-place update when feasible, no explicit compression level.</summary>
    public static readonly ContainerPatchOptions Default = new();

    /// <summary>Initializes a new instance of the <see cref="ContainerPatchOptions"/> class.</summary>
    public ContainerPatchOptions()
    {
    }

    /// <summary>
    /// Gets a value indicating whether the implementation should attempt an in-place
    /// update when the container format allows it, rather than rewriting the whole
    /// archive. When <see langword="false"/>, a full repack is always performed.
    /// </summary>
    public bool PreferInPlace { get; init; } = true;

    /// <summary>
    /// Gets the compression level to use when re-compressing entries. When
    /// <see langword="null"/>, the implementation chooses a default.
    /// </summary>
    public CompressionLevel? CompressionLevel { get; init; }
}