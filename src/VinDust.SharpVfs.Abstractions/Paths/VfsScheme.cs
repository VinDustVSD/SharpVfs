using VinDust.SharpVfs.Abstractions.Exceptions;

namespace VinDust.SharpVfs.Abstractions.Paths;

/// <summary>
/// Represents a URI scheme (RFC 3986 §3.1) identifying a VFS world in a <see cref="VfsUri"/>.
/// </summary>
/// <remarks>
/// <para>
/// A scheme is a short lowercase identifier such as <c>mem</c>, <c>file</c>, or <c>zip</c>.
/// The canonical form matches the grammar <c>[a-z][a-z0-9+.-]*</c>. Uppercase letters are
/// accepted on input and folded to lowercase.
/// </para>
/// <para>
/// The default value (<c>default(VfsScheme)</c>) is uninitialized and invalid for use in a
/// <see cref="VfsUri"/>. Properties that require an initialized value throw
/// <see cref="InvalidOperationException"/> when accessed on the default value.
/// </para>
/// </remarks>
public readonly struct VfsScheme : IEquatable<VfsScheme>, IComparable<VfsScheme>
{
    private readonly string? _value;
    private readonly int _hashCode;

    private VfsScheme(string value)
    {
        _value = value;
        _hashCode = StringComparer.Ordinal.GetHashCode(value);
    }

    /// <summary>Gets a value indicating whether this instance is the default (uninitialized) value.</summary>
    public bool IsDefault => _value is null;

    /// <summary>Gets the canonical lowercase string representation of the scheme.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the scheme is the default value.</exception>
    public string Value => _value
        ?? throw new InvalidOperationException("Scheme is not initialized.");

    /// <summary>Parses a scheme from its string representation.</summary>
    /// <param name="value">The scheme string. Uppercase letters are folded to lowercase.</param>
    /// <returns>The parsed <see cref="VfsScheme"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="VfsInvalidPathException">Thrown when <paramref name="value"/> does not conform to the RFC 3986 scheme grammar.</exception>
    public static VfsScheme Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!TryParse(value, out var result))
        {
            throw new VfsInvalidPathException($"Invalid VFS scheme: '{value}'.");
        }

        return result;
    }

    /// <summary>Attempts to parse a scheme from its string representation.</summary>
    /// <param name="value">The scheme string. Uppercase letters are folded to lowercase.</param>
    /// <param name="result">When this method returns, contains the parsed scheme, or <c>default</c> on failure.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? value, out VfsScheme result)
    {
        if (value is null)
        {
            result = default;
            return false;
        }

        return TryParse(value.AsSpan(), out result);
    }

    /// <summary>Attempts to parse a scheme from a character span.</summary>
    /// <param name="value">The scheme characters. Uppercase letters are folded to lowercase.</param>
    /// <param name="result">When this method returns, contains the parsed scheme, or <c>default</c> on failure.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> value, out VfsScheme result)
    {
        result = default;

        if (value.IsEmpty)
        {
            return false;
        }

        char[]? rented = null;
        Span<char> buffer = value.Length <= 128
            ? stackalloc char[value.Length]
            : (rented = System.Buffers.ArrayPool<char>.Shared.Rent(value.Length));

        try
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= 'A' && c <= 'Z')
                {
                    c = (char)(c + ('a' - 'A'));
                }

                buffer[i] = c;
            }

            Span<char> normalized = buffer[..value.Length];

            if (!IsValid(normalized))
            {
                return false;
            }

            result = new VfsScheme(new string(normalized));
            return true;
        }
        finally
        {
            if (rented is not null)
            {
                System.Buffers.ArrayPool<char>.Shared.Return(rented);
            }
        }
    }

    private static bool IsValid(ReadOnlySpan<char> value)
    {
        char first = value[0];
        if (first < 'a' || first > 'z')
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            char c = value[i];
            bool ok = (c >= 'a' && c <= 'z')
                   || (c >= '0' && c <= '9')
                   || c == '+'
                   || c == '-'
                   || c == '.';

            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public bool Equals(VfsScheme other)
        => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is VfsScheme other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _hashCode;

    /// <inheritdoc />
    public int CompareTo(VfsScheme other)
        => string.CompareOrdinal(_value, other._value);

    /// <summary>Returns the canonical lowercase string representation of the scheme, or the empty string for the default value.</summary>
    /// <returns>The canonical scheme string.</returns>
    public override string ToString() => _value ?? string.Empty;

    /// <summary>Determines whether two schemes are equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the schemes are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(VfsScheme left, VfsScheme right) => left.Equals(right);

    /// <summary>Determines whether two schemes are not equal.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if the schemes are not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(VfsScheme left, VfsScheme right) => !left.Equals(right);

    /// <summary>Determines whether <paramref name="left"/> is lexicographically less than <paramref name="right"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> precedes <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator <(VfsScheme left, VfsScheme right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether <paramref name="left"/> is lexicographically less than or equal to <paramref name="right"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> precedes or equals <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator <=(VfsScheme left, VfsScheme right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether <paramref name="left"/> is lexicographically greater than <paramref name="right"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> follows <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator >(VfsScheme left, VfsScheme right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether <paramref name="left"/> is lexicographically greater than or equal to <paramref name="right"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> follows or equals <paramref name="right"/>; otherwise <see langword="false"/>.</returns>
    public static bool operator >=(VfsScheme left, VfsScheme right) => left.CompareTo(right) >= 0;
}
