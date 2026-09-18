using VinDust.SharpVfs.Abstractions.Exceptions;

namespace VinDust.SharpVfs.Abstractions.Paths;

/// <summary>
/// A relative path used for navigation within a file system.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="RelativePath"/> has no scheme and no leading slash. Segments
/// <c>.</c> and <c>..</c> are recognized. During parsing, <c>.</c> segments are
/// dropped (they are navigational no-ops), while <c>..</c> segments are preserved
/// as parent-directory steps.
/// </para>
/// <para>
/// An instance with zero segments (produced by parsing <c>""</c> or <c>"."</c>)
/// represents the current directory and is available as <see cref="Current"/>.
/// </para>
/// <para>
/// Use <see cref="ResolveAgainst(FsPath)"/> to compute an absolute path. Resolution
/// clamps at the root: excess <c>..</c> steps are silently ignored.
/// </para>
/// <para>
/// The default value (<c>default(RelativePath)</c>) is uninitialized. All members
/// that require an initialized value throw <see cref="InvalidOperationException"/>
/// on the default value.
/// </para>
/// </remarks>
public readonly struct RelativePath : IEquatable<RelativePath>, IComparable<RelativePath>
{
    /// <summary>A relative path referring to the current directory (zero segments).</summary>
    public static readonly RelativePath Current = new(Array.Empty<string>());

    private readonly string[]? _segments;
    private readonly int _hashCode;

    private RelativePath(string[] segments)
    {
        _segments = segments;
        _hashCode = ComputeHash(segments);
    }

    /// <summary>Gets a value indicating whether this instance is the default (uninitialized) value.</summary>
    public bool IsDefault => _segments is null;

    /// <summary>Gets a value indicating whether this path refers to the current directory.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the path is the default value.</exception>
    public bool IsCurrent => EnsureSegments().Length == 0;

    /// <summary>Gets the number of segments.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the path is the default value.</exception>
    public int SegmentCount => EnsureSegments().Length;

    /// <summary>Gets the segments of this path, in root-to-leaf order.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the path is the default value.</exception>
    public ReadOnlySpan<string> Segments => EnsureSegments();

    /// <summary>Parses a relative path.</summary>
    /// <param name="value">The path string, e.g. <c>"../assets/foo.png"</c>. The empty string and <c>"."</c> produce <see cref="Current"/>.</param>
    /// <returns>The parsed <see cref="RelativePath"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="VfsInvalidPathException">Thrown when <paramref name="value"/> is not a valid relative path.</exception>
    public static RelativePath Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!TryParse(value, out var result))
        {
            throw new VfsInvalidPathException($"Invalid relative path: '{value}'.");
        }

        return result;
    }

    /// <summary>Attempts to parse a relative path.</summary>
    /// <param name="value">The path string.</param>
    /// <param name="result">When this method returns, contains the parsed path, or <c>default</c> on failure.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// Rejected inputs: leading slash, trailing slash, empty segments (double slash),
    /// control characters (U+0000..U+001F, U+007F).
    /// </remarks>
    public static bool TryParse(string? value, out RelativePath result)
    {
        result = default;

        if (value is null)
        {
            return false;
        }

        if (value.Length == 0)
        {
            result = Current;
            return true;
        }

        if (value[0] == '/' || value[^1] == '/')
        {
            return false;
        }

        // First pass: validate and count segments, plus count '.' segments to drop.
        int totalSegments = 1;
        int dotOnlySegments = 0;
        int start = 0;

        for (int i = 0; i <= value.Length; i++)
        {
            if (i != value.Length && value[i] != '/')
            {
                continue;
            }

            int length = i - start;
            if (length == 0)
            {
                return false;
            }

            var span = value.AsSpan(start, length);
            if (!IsValidSegment(span))
            {
                return false;
            }

            if (length == 1 && span[0] == '.')
            {
                dotOnlySegments++;
            }

            if (i != value.Length)
            {
                totalSegments++;
            }

            start = i + 1;
        }

        int keptCount = totalSegments - dotOnlySegments;
        if (keptCount == 0)
        {
            result = Current;
            return true;
        }

        // Second pass: materialize non-'.' segments.
        var segments = new string[keptCount];
        int index = 0;
        start = 0;

        for (int i = 0; i <= value.Length; i++)
        {
            if (i != value.Length && value[i] != '/')
            {
                continue;
            }

            int length = i - start;
            var span = value.AsSpan(start, length);
            if (!(length == 1 && span[0] == '.'))
            {
                segments[index++] = new string(span);
            }

            start = i + 1;
        }

        result = new RelativePath(segments);
        return true;
    }

    /// <summary>Resolves this relative path against an absolute file system path.</summary>
    /// <param name="basePath">The base path.</param>
    /// <returns>
    /// The resolved <see cref="FsPath"/>. <c>..</c> steps that would escape the root
    /// are clamped; the result is always absolute.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when this path is the default value.</exception>
    public FsPath ResolveAgainst(FsPath basePath)
    {
        var relSegments = EnsureSegments();
        var baseSegments = basePath.Segments;

        var buffer = new string[baseSegments.Length + relSegments.Length];
        int count = 0;

        for (int i = 0; i < baseSegments.Length; i++)
        {
            buffer[count++] = baseSegments[i];
        }

        foreach (string segment in relSegments)
        {
            if (segment.Length == 2 && segment[0] == '.' && segment[1] == '.')
            {
                if (count > 0)
                {
                    count--;
                }
            }
            else
            {
                buffer[count++] = segment;
            }
        }

        return FsPath.FromSegments(buffer.AsSpan(0, count));
    }

    /// <summary>Resolves this relative path against an absolute VFS URI.</summary>
    /// <param name="baseUri">The base URI.</param>
    /// <returns>
    /// The resolved <see cref="VfsUri"/>, preserving the scheme of <paramref name="baseUri"/>.
    /// <c>..</c> steps that would escape the root are clamped.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when this path is the default value.</exception>
    public VfsUri ResolveAgainst(VfsUri baseUri)
    {
        var resolvedPath = ResolveAgainst(baseUri.Path);
        return VfsUri.FromParts(baseUri.Scheme, resolvedPath);
    }

    /// <inheritdoc />
    public bool Equals(RelativePath other)
    {
        if (ReferenceEquals(_segments, other._segments))
        {
            return true;
        }

        var mine = _segments;
        var theirs = other._segments;

        if (mine is null || theirs is null || mine.Length != theirs.Length)
        {
            return false;
        }

        for (int i = 0; i < mine.Length; i++)
        {
            if (!string.Equals(mine[i], theirs[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RelativePath other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _hashCode;

    /// <inheritdoc />
    public int CompareTo(RelativePath other)
    {
        var a = _segments ?? Array.Empty<string>();
        var b = other._segments ?? Array.Empty<string>();

        int min = Math.Min(a.Length, b.Length);
        for (int i = 0; i < min; i++)
        {
            int cmp = string.CompareOrdinal(a[i], b[i]);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    /// <summary>Returns the canonical string form of this path. The current directory is rendered as <c>"."</c>.</summary>
    /// <returns>The canonical string form, or the empty string for the default value.</returns>
    public override string ToString()
    {
        if (_segments is null)
        {
            return string.Empty;
        }

        if (_segments.Length == 0)
        {
            return ".";
        }

        return string.Join('/', _segments);
    }

    /// <summary>Determines whether two relative paths are equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the paths are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(RelativePath left, RelativePath right) => left.Equals(right);

    /// <summary>Determines whether two relative paths are not equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the paths are not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(RelativePath left, RelativePath right) => !left.Equals(right);

    /// <summary>Determines whether <paramref name="left"/> precedes <paramref name="right"/> in segment-wise ordinal order.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> precedes <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator <(RelativePath left, RelativePath right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether <paramref name="left"/> precedes or equals <paramref name="right"/> in segment-wise ordinal order.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> precedes or equals <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator <=(RelativePath left, RelativePath right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether <paramref name="left"/> follows <paramref name="right"/> in segment-wise ordinal order.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> follows <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator >(RelativePath left, RelativePath right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether <paramref name="left"/> follows or equals <paramref name="right"/> in segment-wise ordinal order.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> follows or equals <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator >=(RelativePath left, RelativePath right) => left.CompareTo(right) >= 0;

    internal static RelativePath FromSegments(ReadOnlySpan<string> segments)
        => segments.Length == 0 ? Current : new RelativePath(segments.ToArray());

    private string[] EnsureSegments()
        => _segments ?? throw new InvalidOperationException("Path is not initialized.");

    private static bool IsValidSegment(ReadOnlySpan<char> segment)
    {
        if (segment.Length == 0)
        {
            return false;
        }

        foreach (char c in segment)
        {
            if (c < 0x20 || c == 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    private static int ComputeHash(string[] segments)
    {
        var hash = default(HashCode);
        foreach (string segment in segments)
        {
            hash.Add(segment, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
