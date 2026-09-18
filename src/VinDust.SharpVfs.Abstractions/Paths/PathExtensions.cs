namespace VinDust.SharpVfs.Abstractions.Paths;

/// <summary>
/// Convenience extensions for parsing path-like strings into strongly typed path values.
/// </summary>
/// <remarks>
/// These extensions exist purely for ergonomics at call sites. Library APIs never accept
/// <see cref="string"/> for path parameters; use <see cref="VfsUri"/>, <see cref="FsPath"/>,
/// or <see cref="RelativePath"/> directly.
/// </remarks>
public static class PathExtensions
{
    /// <summary>Parses a VFS URI from its string representation.</summary>
    /// <param name="value">The URI string.</param>
    /// <returns>The parsed <see cref="VfsUri"/>.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception name="VfsInvalidPathException">Thrown when <paramref name="value"/> is not a valid VFS URI.</exception>
    public static VfsUri ToVfsUri(this string value) => VfsUri.Parse(value);

    /// <summary>Parses an absolute file system path from its string representation.</summary>
    /// <param name="value">The path string.</param>
    /// <returns>The parsed <see cref="FsPath"/>.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception name="VfsInvalidPathException">Thrown when <paramref name="value"/> is not a valid absolute path.</exception>
    public static FsPath ToFsPath(this string value) => FsPath.Parse(value);

    /// <summary>Parses a relative path from its string representation.</summary>
    /// <param name="value">The path string.</param>
    /// <returns>The parsed <see cref="RelativePath"/>.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception name="VfsInvalidPathException">Thrown when <paramref name="value"/> is not a valid relative path.</exception>
    public static RelativePath ToRelativePath(this string value) => RelativePath.Parse(value);
}
