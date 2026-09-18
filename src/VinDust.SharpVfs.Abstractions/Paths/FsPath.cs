using VinDust.SharpVfs.Abstractions.Exceptions;

namespace VinDust.SharpVfs.Abstractions.Paths;

/// <summary>
/// An absolute path within a single file system, without a scheme.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FsPath"/> is the low-level identifier used by <see cref="IFileSystem"/>.
/// It always begins with <c>/</c>, contains no scheme, and cannot contain
/// <c>.</c> or <c>..</c> segments. The root path is represented by <c>"/"</c>.
/// </para>
/// <para>
/// For an identifier addressable across the entire VFS tree, use <see cref="VfsUri"/>.
/// For navigation, use <see cref="RelativePath"/>.
/// </para>
/// <para>
/// The default value (<c>default(FsPath)</c>) is uninitialized. All members that require
/// an initialized path throw <see cref="InvalidOperationException"/> on the default value.
/// </para>
/// </remarks>
public readonly struct FsPath : IEquatable<FsPath>, IComparable<FsPath>
{
    /// <summary>The root path <c>"/"</c>.</summary>
    public static readonly FsPath Root = new(Array.Empty<string>());

    private readonly string[]? _segments;
    private readonly int _hashCode;

    private FsPath(string[] segments)
    {
        _segments = segments;
        _hashCode = ComputeHash(segments);
    }

    internal static FsPath FromSegments(ReadOnlySpan<string> segments)
        => segments.Length == 0 ? Root : new FsPath(segments.ToArray());

    /// <summary>Gets a value indicating whether this instance is the default (uninitialized) value.</summary>
    public bool IsDefault => _segments is null;

    /// <summary>Gets a value indicating whether this path represents the root (<c>"/"</c>).</summary>
    /// <exception cref="InvalidOperationException">Thrown when the path is the default value.</exception>
    public bool IsRoot => EnsureSegments().Length == 0;

    /// <summary>Gets the number of path segments. The root has zero segments.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the path is the default value.</exception>
    public int SegmentCount => EnsureSegments().Length;

    /// <summary>Gets the segments of this path, in root-to-leaf order.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the path is the default value.</exception>
    public ReadOnlySpan<string> Segments => EnsureSegments();

    /// <summary>Parses an absolute file system path.</summary>
    /// <param name="value">The path string, e.g. <c>"/mods/config.json"</c>.</param>
    /// <returns>The parsed <see cref="FsPath"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="VfsInvalidPathException">Thrown when <paramref name="value"/> is not a valid absolute path.</exception>
    public static FsPath Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!TryParse(value, out var result))
        {
            throw new VfsInvalidPathException($"Invalid file system path: '{value}'.");
        }

        return result;
    }

    /// <summary>Attempts to parse an absolute file system path.</summary>
    /// <param name="value">The path string.</param>
    /// <param name="result">When this method returns, contains the parsed path, or <c>default</c> on failure.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? value, out FsPath result)
    {
        result = default;

        if (string.IsNullOrEmpty(value) || value[0] != '/')
        {
            return false;
        }

        return TryParseBody(value.AsSpan(1), out result);
    }

    internal static bool TryParseBody(ReadOnlySpan<char> body, out FsPath result)
    {
        result = default;

        if (body.Length == 0)
        {
            result = Root;
            return true;
        }

        if (body[^1] == '/')
        {
            return false;
        }

        int totalSegments = 1;
        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] == '/')
            {
                totalSegments++;
            }
        }

        var segments = new string[totalSegments];
        int index = 0;
        int start = 0;

        for (int i = 0; i <= body.Length; i++)
        {
            if (i != body.Length && body[i] != '/')
            {
                continue;
            }

            int length = i - start;
            if (length == 0)
            {
                return false;
            }

            var span = body.Slice(start, length);
            if (!IsValidSegment(span))
            {
                return false;
            }

            segments[index++] = new string(span);
            start = i + 1;
        }

        result = new FsPath(segments);
        return true;
    }

    private static bool IsValidSegment(ReadOnlySpan<char> segment)
    {
        if (segment.Length == 0)
        {
            return false;
        }

        if (segment.Length == 1 && segment[0] == '.')
        {
            return false;
        }

        if (segment.Length == 2 && segment[0] == '.' && segment[1] == '.')
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

    /// <summary>Returns the parent path, or <see langword="null"/> if this path is the root.</summary>
    /// <returns>The parent path, or <see langword="null"/> for the root.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the path is the default value.</exception>
    public FsPath? GetParent()
    {
        var segments = EnsureSegments();

        if (segments.Length == 0)
        {
            return null;
        }

        if (segments.Length == 1)
        {
            return Root;
        }

        var parent = new string[segments.Length - 1];
        Array.Copy(segments, parent, parent.Length);
        return new FsPath(parent);
    }

    /// <summary>Returns the last segment of this path, or <see langword="null"/> for the root.</summary>
    /// <returns>The file name, or <see langword="null"/> for the root.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the path is the default value.</exception>
    public string? GetFileName()
    {
        var segments = EnsureSegments();
        return segments.Length == 0 ? null : segments[^1];
    }

    /// <summary>Returns a sub-path composed of <paramref name="count"/> segments starting at <paramref name="start"/>.</summary>
    /// <param name="start">The zero-based index of the first segment.</param>
    /// <param name="count">The number of segments to include. Zero produces the root.</param>
    /// <returns>The sub-path.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="start"/> or <paramref name="count"/> is out of range.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the path is the default value.</exception>
    public FsPath SubPath(int start, int count)
    {
        var segments = EnsureSegments();

        if ((uint)start > (uint)segments.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if ((uint)count > (uint)(segments.Length - start))
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 0)
        {
            return Root;
        }

        var slice = new string[count];
        Array.Copy(segments, start, slice, 0, count);
        return new FsPath(slice);
    }

    /// <summary>Determines whether this path starts with the given path, comparing by segments.</summary>
    /// <param name="other">The prefix to test.</param>
    /// <returns><see langword="true"/> if <paramref name="other"/> is a prefix of this path (or equal to it); otherwise <see langword="false"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when either path is the default value.</exception>
    public bool StartsWith(FsPath other)
    {
        var mine = EnsureSegments();
        var theirs = other.Segments;

        if (theirs.Length > mine.Length)
        {
            return false;
        }

        for (int i = 0; i < theirs.Length; i++)
        {
            if (!string.Equals(mine[i], theirs[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Expresses this path relative to a base path.</summary>
    /// <param name="basePath">The base path.</param>
    /// <returns>
    /// A <see cref="RelativePath"/> that, when resolved against <paramref name="basePath"/>, yields this path.
    /// Returns <see cref="RelativePath.Current"/> when both paths are equal.
    /// </returns>
    /// <exception cref="InvalidOperationException">Thrown when either path is the default value.</exception>
    public RelativePath RelativeTo(FsPath basePath)
    {
        var mine = EnsureSegments();
        var theirs = basePath.Segments;

        int common = 0;
        int min = Math.Min(mine.Length, theirs.Length);
        while (common < min && string.Equals(mine[common], theirs[common], StringComparison.Ordinal))
        {
            common++;
        }

        int ups = theirs.Length - common;
        int downs = mine.Length - common;

        if (ups == 0 && downs == 0)
        {
            return RelativePath.Current;
        }

        var segments = new string[ups + downs];
        for (int i = 0; i < ups; i++)
        {
            segments[i] = "..";
        }

        for (int i = 0; i < downs; i++)
        {
            segments[ups + i] = mine[common + i];
        }

        return RelativePath.FromSegments(segments);
    }

    /// <inheritdoc />
    public bool Equals(FsPath other)
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
    public override bool Equals(object? obj) => obj is FsPath other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _hashCode;

    /// <inheritdoc />
    public int CompareTo(FsPath other)
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

    /// <summary>Returns the canonical string form of this path.</summary>
    /// <returns>The canonical path, or the empty string for the default value.</returns>
    public override string ToString()
    {
        if (_segments is null)
        {
            return string.Empty;
        }

        if (_segments.Length == 0)
        {
            return "/";
        }

        return "/" + string.Join('/', _segments);
    }

    private string[] EnsureSegments()
        => _segments ?? throw new InvalidOperationException("Path is not initialized.");

    private static int ComputeHash(string[] segments)
    {
        var hash = default(HashCode);
        foreach (string segment in segments)
        {
            hash.Add(segment, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <summary>Determines whether two paths are equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the paths are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(FsPath left, FsPath right) => left.Equals(right);

    /// <summary>Determines whether two paths are not equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the paths are not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(FsPath left, FsPath right) => !left.Equals(right);

    /// <summary>Determines whether <paramref name="left"/> precedes <paramref name="right"/> in segment-wise ordinal order.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> precedes <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator <(FsPath left, FsPath right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether <paramref name="left"/> precedes or equals <paramref name="right"/> in segment-wise ordinal order.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> precedes or equals <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator <=(FsPath left, FsPath right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether <paramref name="left"/> follows <paramref name="right"/> in segment-wise ordinal order.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> follows <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator >(FsPath left, FsPath right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether <paramref name="left"/> follows or equals <paramref name="right"/> in segment-wise ordinal order.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> follows or equals <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator >=(FsPath left, FsPath right) => left.CompareTo(right) >= 0;
}
