using FluentAssertions;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;
using Xunit;

namespace VinDust.SharpVfs.Abstractions.Tests.Paths;

/// <summary>Tests for <see cref="VfsUri"/>.</summary>
public sealed class VfsUriTests
{
    [Theory]
    [InlineData("mem:///", "mem", 0)]
    [InlineData("mem:///foo", "mem", 1)]
    [InlineData("mem:///foo/bar", "mem", 2)]
    [InlineData("file:///a/b/c", "file", 3)]
    [InlineData("MEM:///foo", "mem", 1)]                    // scheme folded
    [InlineData("zip:///archive/entry.txt", "zip", 2)]
    public void Parse_AcceptsValidUris(string input, string expectedScheme, int expectedSegments)
    {
        var uri = VfsUri.Parse(input);

        uri.IsDefault.Should().BeFalse();
        uri.Scheme.Value.Should().Be(expectedScheme);
        uri.SegmentCount.Should().Be(expectedSegments);
    }

    [Theory]
    [InlineData("mem://foo")]              // two slashes
    [InlineData("mem:/foo")]               // one slash
    [InlineData("mem:foo")]                // no slashes
    [InlineData("mem:////foo")]            // four slashes
    [InlineData(":///foo")]                // empty scheme
    [InlineData("/foo")]                   // no scheme
    [InlineData("foo")]                    // no scheme, no slash
    [InlineData("mem:///foo/")]            // trailing slash
    [InlineData("mem:///foo//bar")]        // double slash
    [InlineData("mem:///foo/./bar")]       // '.' segment
    [InlineData("mem:///foo/../bar")]      // '..' segment
    [InlineData("1mem:///foo")]            // invalid scheme
    [InlineData("mem:///foo\x00")]         // NUL
    [InlineData("mem:///foo\x1F")]         // control
    public void Parse_RejectsInvalidUris(string input)
    {
        var act = () => VfsUri.Parse(input);

        act.Should().Throw<VfsInvalidPathException>();
    }

    [Fact]
    public void Parse_Null_Throws()
    {
        var act = () => VfsUri.Parse(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Parse_Root_HasEmptySegments()
    {
        var uri = VfsUri.Parse("mem:///");

        uri.IsRoot.Should().BeTrue();
        uri.SegmentCount.Should().Be(0);
        uri.GetFileName().Should().BeNull();
        uri.GetParent().Should().BeNull();
    }

    [Fact]
    public void TryParse_ReturnsFalse_OnInvalidInput()
    {
        VfsUri.TryParse("mem://foo", out var uri).Should().BeFalse();
        uri.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void TryParse_Null_ReturnsFalse()
    {
        VfsUri.TryParse(null, out _).Should().BeFalse();
    }

    [Fact]
    public void Scheme_OnDefault_Throws()
    {
        var uri = default(VfsUri);

        var act = () => uri.Scheme;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Path_OnDefault_Throws()
    {
        var uri = default(VfsUri);

        var act = () => uri.Path;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Segments_OnDefault_Throws()
    {
        var uri = default(VfsUri);

        Action act = () => _ = uri.Segments;

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("mem:///", null)]                    // root has no parent
    [InlineData("mem:///foo", "mem:///")]            // one segment -> root
    [InlineData("mem:///a/b", "mem:///a")]
    [InlineData("mem:///a/b/c", "mem:///a/b")]
    public void GetParent_ReturnsExpected(string input, string? expected)
    {
        var uri = VfsUri.Parse(input);

        var parent = uri.GetParent();

        if (expected is null)
        {
            parent.Should().BeNull();
        }
        else
        {
            parent.Should().Be(VfsUri.Parse(expected));
        }
    }

    [Theory]
    [InlineData("mem:///", null)]
    [InlineData("mem:///foo", "foo")]
    [InlineData("mem:///a/b", "b")]
    public void GetFileName_ReturnsExpected(string input, string? expected)
    {
        VfsUri.Parse(input).GetFileName().Should().Be(expected);
    }

    [Fact]
    public void SubPath_PreservesScheme()
    {
        var uri = VfsUri.Parse("mem:///a/b/c/d");

        var sub = uri.SubPath(1, 2);

        sub.Should().Be(VfsUri.Parse("mem:///b/c"));
        sub.Scheme.Value.Should().Be("mem");
    }

    [Fact]
    public void StartsWith_SameSchemeAndPrefix()
    {
        var uri = VfsUri.Parse("mem:///a/b/c");

        uri.StartsWith(VfsUri.Parse("mem:///")).Should().BeTrue();
        uri.StartsWith(VfsUri.Parse("mem:///a")).Should().BeTrue();
        uri.StartsWith(VfsUri.Parse("mem:///a/b/c")).Should().BeTrue();
        uri.StartsWith(VfsUri.Parse("mem:///a/b/c/d")).Should().BeFalse();
    }

    [Fact]
    public void StartsWith_DifferentScheme_ReturnsFalse()
    {
        var uri = VfsUri.Parse("mem:///a/b");

        uri.StartsWith(VfsUri.Parse("file:///a")).Should().BeFalse();
    }

    [Fact]
    public void StartsWith_DoesNotMatchByStringPrefix()
    {
        VfsUri.Parse("mem:///foobar").StartsWith(VfsUri.Parse("mem:///foo")).Should().BeFalse();
    }

    [Fact]
    public void RelativeTo_SameScheme_ReturnsRelative()
    {
        var uri = VfsUri.Parse("mem:///a/b/c");
        var baseUri = VfsUri.Parse("mem:///a");

        var rel = uri.RelativeTo(baseUri);

        rel.Should().NotBeNull();
        rel!.Value.ToString().Should().Be("b/c");
    }

    [Fact]
    public void RelativeTo_DifferentScheme_ReturnsNull()
    {
        var uri = VfsUri.Parse("mem:///a");
        var baseUri = VfsUri.Parse("file:///a");

        uri.RelativeTo(baseUri).Should().BeNull();
    }

    [Fact]
    public void Equality_ComparesSchemeAndPath()
    {
        var a = VfsUri.Parse("mem:///a/b");
        var b = VfsUri.Parse("MEM:///a/b");    // scheme case-insensitive
        var c = VfsUri.Parse("mem:///a/c");
        var d = VfsUri.Parse("file:///a/b");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.Should().NotBe(c);
        a.Should().NotBe(d);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Default_EqualsDefault()
    {
        var a = default(VfsUri);
        var b = default(VfsUri);

        a.Should().Be(b);
        a.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void CompareTo_SchemeFirst_ThenPath()
    {
        var file = VfsUri.Parse("file:///z");
        var mem = VfsUri.Parse("mem:///a");

        file.CompareTo(mem).Should().BeNegative();

        var memA = VfsUri.Parse("mem:///a");
        var memB = VfsUri.Parse("mem:///b");

        memA.CompareTo(memB).Should().BeNegative();
    }

    [Theory]
    [InlineData("mem:///")]
    [InlineData("mem:///foo")]
    [InlineData("mem:///a/b/c")]
    [InlineData("file:///a/b")]
    public void ToString_RoundTrips(string input)
    {
        var uri = VfsUri.Parse(input);

        uri.ToString().Should().Be(input);
        VfsUri.Parse(uri.ToString()).Should().Be(uri);
    }

    [Fact]
    public void ToString_FoldsSchemeToLowercase()
    {
        VfsUri.Parse("MEM:///foo").ToString().Should().Be("mem:///foo");
    }

    [Fact]
    public void ToString_OnDefault_ReturnsEmpty()
    {
        default(VfsUri).ToString().Should().Be(string.Empty);
    }
}
