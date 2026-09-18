using VinDust.SharpVfs.Abstractions.Exceptions;

namespace VinDust.SharpVfs.Abstractions.Paths;

/// <summary>
/// A globally unique identifier of a node within a <c>VfsRoot</c>.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="VfsUri"/> combines a <see cref="VfsScheme"/> identifying the VFS world
/// with an absolute <see cref="FsPath"/> within that world. The canonical textual form is
/// <c>&lt;scheme&gt;:///&lt;path&gt;</c>, for example <c>mem:///mods/config.json</c> or
/// <c>file:///</c> for the root of a <c>file</c> world.
/// </para>
/// <para>
/// Exactly three slashes are required after the scheme (URI convention for an empty
/// authority). Two slashes (<c>mem://foo</c>) or four or more (<c>mem:////foo</c>) are
/// rejected.
/// </para>
/// <para>
/// The default value (<c>default(VfsUri)</c>) is uninitialized. All members that require
/// an initialized value throw <see cref="InvalidOperationException"/> on the default value.
/// </para>
/// </remarks>
public readonly struct VfsUri : IEquatable<VfsUri>, IComparable<VfsUri>
{
    private readonly VfsScheme _scheme;
    private readonly FsPath _path;
    private readonly int _hashCode;

    private VfsUri(VfsScheme scheme, FsPath path)
    {
        _scheme = scheme;
        _path = path;
        _hashCode = HashCode.Combine(scheme, path);
    }

    /// <summary>Gets a value indicating whether this instance is the default (uninitialized) value.</summary>
    public bool IsDefault => _scheme.IsDefault;

    /// <summary>Gets the scheme identifying the VFS world.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the URI is the default value.</exception>
    public VfsScheme Scheme => IsDefault
        ? throw new InvalidOperationException("VfsUri is not initialized.")
        : _scheme;

    /// <summary>Gets the absolute path within the VFS world.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the URI is the default value.</exception>
    public FsPath Path => IsDefault
        ? throw new InvalidOperationException("VfsUri is not initialized.")
        : _path;

    /// <summary>Gets a value indicating whether this URI addresses the root of its VFS world.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the URI is the default value.</exception>
    public bool IsRoot => Path.IsRoot;

    /// <summary>Gets the number of path segments. The root has zero segments.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the URI is the default value.</exception>
    public int SegmentCount => Path.SegmentCount;

    /// <summary>Gets the path segments, in root-to-leaf order.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the URI is the default value.</exception>
    public ReadOnlySpan<string> Segments => Path.Segments;

    /// <summary>Parses a VFS URI.</summary>
    /// <param name="value">The URI string, e.g. <c>"mem:///mods/config.json"</c>.</param>
    /// <returns>The parsed <see cref="VfsUri"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="VfsInvalidPathException">Thrown when <paramref name="value"/> is not a valid VFS URI.</exception>
    public static VfsUri Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!TryParse(value, out var result))
        {
            throw new VfsInvalidPathException($"Invalid VFS URI: '{value}'.");
        }

        return result;
    }

    /// <summary>Attempts to parse a VFS URI.</summary>
    /// <param name="value">The URI string.</param>
    /// <param name="result">When this method returns, contains the parsed URI, or <c>default</c> on failure.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Rejected inputs: missing scheme, missing or malformed <c>:///</c> separator
    /// (must be exactly three slashes), control characters (U+0000..U+001F, U+007F),
    /// <c>.</c> or <c>..</c> path segments.
    /// </remarks>
    public static bool TryParse(string? value, out VfsUri result)
    {
        result = default;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (char c in value)
        {
            if (c < 0x20 || c == 0x7F)
            {
                return false;
            }
        }

        int colon = value.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        // Require exactly ":///" after the scheme.
        if (value.Length < colon + 4)
        {
            return false;
        }

        if (value[colon + 1] != '/' || value[colon + 2] != '/' || value[colon + 3] != '/')
        {
            return false;
        }

        // The character after ":///" (if present) must not be another slash.
        if (value.Length > colon + 4 && value[colon + 4] == '/')
        {
            return false;
        }

        if (!VfsScheme.TryParse(value.AsSpan(0, colon), out var scheme))
        {
            return false;
        }

        if (!FsPath.TryParseBody(value.AsSpan(colon + 4), out var path))
        {
            return false;
        }

        result = new VfsUri(scheme, path);
        return true;
    }

    /// <summary>Returns the parent URI, or <see langword="null"/> if this URI is the root.</summary>
    /// <returns>The parent URI, or <see langword="null"/> for the root.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the URI is the default value.</exception>
    public VfsUri? GetParent()
    {
        var parent = Path.GetParent();
        return parent is null ? null : new VfsUri(_scheme, parent.Value);
    }

    /// <summary>Returns the last path segment, or <see langword="null"/> for the root.</summary>
    /// <returns>The file name, or <see langword="null"/> for the root.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the URI is the default value.</exception>
    public string? GetFileName() => Path.GetFileName();

    /// <summary>Returns a sub-URI composed of <paramref name="count"/> segments starting at <paramref name="start"/>.</summary>
    /// <param name="start">The zero-based index of the first segment.</param>
    /// <param name="count">The number of segments to include. Zero produces the root.</param>
    /// <returns>The sub-URI, with the same scheme.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="start"/> or <paramref name="count"/> is out of range.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the URI is the default value.</exception>
    public VfsUri SubPath(int start, int count)
        => new(_scheme, Path.SubPath(start, count));

    /// <summary>Determines whether this URI starts with the given URI, comparing by scheme and path segments.</summary>
    /// <param name="other">The prefix to test.</param>
    /// <returns><see langword="true"/> if <paramref name="other"/> is a prefix of this URI (or equal to it); otherwise <see langword="false"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when either URI is the default value.</exception>
    public bool StartsWith(VfsUri other)
        => _scheme == other._scheme && _path.StartsWith(other._path);

    /// <summary>Expresses this URI relative to a base URI.</summary>
    /// <param name="baseUri">The base URI. Its scheme must match this URI's scheme.</param>
    /// <returns>A <see cref="RelativePath"/> that, when resolved against <paramref name="baseUri"/>, yields this URI; or <see langword="null"/> when the schemes differ.</returns>
    /// <exception cref="InvalidOperationException">Thrown when either URI is the default value.</exception>
    public RelativePath? RelativeTo(VfsUri baseUri)
        => _scheme == baseUri._scheme ? _path.RelativeTo(baseUri._path) : null;

    /// <inheritdoc />
    public bool Equals(VfsUri other) => _scheme == other._scheme && _path == other._path;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is VfsUri other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _hashCode;

    /// <inheritdoc />
    public int CompareTo(VfsUri other)
    {
        int cmp = _scheme.CompareTo(other._scheme);
        return cmp != 0 ? cmp : _path.CompareTo(other._path);
    }

    /// <summary>Returns the canonical textual form of this URI, or the empty string for the default value.</summary>
    /// <returns>The canonical URI string.</returns>
    public override string ToString()
    {
        if (IsDefault)
        {
            return string.Empty;
        }

        // _path.ToString() always begins with '/', so "scheme://" + "/foo" yields "scheme:///foo".
        return $"{_scheme}://{_path}";
    }

    /// <summary>Determines whether two URIs are equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the URIs are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(VfsUri left, VfsUri right) => left.Equals(right);

    /// <summary>Determines whether two URIs are not equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the URIs are not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(VfsUri left, VfsUri right) => !left.Equals(right);

    /// <summary>Determines whether <paramref name="left"/> precedes <paramref name="right"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> precedes <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator <(VfsUri left, VfsUri right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether <paramref name="left"/> precedes or equals <paramref name="right"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> precedes or equals <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator <=(VfsUri left, VfsUri right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether <paramref name="left"/> follows <paramref name="right"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> follows <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator >(VfsUri left, VfsUri right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether <paramref name="left"/> follows or equals <paramref name="right"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> follows or equals <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator >=(VfsUri left, VfsUri right) => left.CompareTo(right) >= 0;

    internal static VfsUri FromParts(VfsScheme scheme, FsPath path)
    {
        if (scheme.IsDefault)
        {
            throw new ArgumentException("Scheme must be initialized.", nameof(scheme));
        }

        if (path.IsDefault)
        {
            throw new ArgumentException("Path must be initialized.", nameof(path));
        }

        return new VfsUri(scheme, path);
    }
}
