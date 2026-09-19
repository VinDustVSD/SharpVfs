using FluentAssertions;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;
using Xunit;

namespace VinDust.SharpVfs.Abstractions.Tests.Paths;

/// <summary>Tests for <see cref="RelativePath"/>.</summary>
public sealed class RelativePathTests
{
    [Theory]
    [InlineData("", 0)]                    // empty -> Current
    [InlineData(".", 0)]                   // '.' -> Current
    [InlineData("foo", 1)]
    [InlineData("foo/bar", 2)]
    [InlineData("../foo", 2)]
    [InlineData("../../foo", 3)]
    [InlineData("./foo", 1)]               // '.' dropped
    [InlineData("foo/./bar", 2)]           // '.' dropped
    [InlineData("foo/../bar", 3)]          // '..' preserved
    public void Parse_AcceptsValidPaths(string input, int expectedSegments)
    {
        var path = RelativePath.Parse(input);

        path.IsDefault.Should().BeFalse();
        path.SegmentCount.Should().Be(expectedSegments);
    }

    [Theory]
    [InlineData("/foo")]                   // leading slash
    [InlineData("foo/")]                   // trailing slash
    [InlineData("foo//bar")]               // double slash
    [InlineData("foo\x00")]                // NUL
    [InlineData("foo\x1F")]                // control char
    [InlineData("foo\x7F")]                // DEL
    public void Parse_RejectsInvalidPaths(string input)
    {
        var act = () => RelativePath.Parse(input);

        act.Should().Throw<VfsInvalidPathException>();
    }

    [Fact]
    public void Parse_Null_Throws()
    {
        var act = () => RelativePath.Parse(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Parse_EmptyAndDot_ReturnCurrent()
    {
        RelativePath.Parse(string.Empty).Should().Be(RelativePath.Current);
        RelativePath.Parse(".").Should().Be(RelativePath.Current);
    }

    [Fact]
    public void Parse_TrailingSlash_Throws()
    {
        var act = () => RelativePath.Parse("./././");

        act.Should().Throw<VfsInvalidPathException>();
    }

    [Fact]
    public void TryParse_Null_ReturnsFalse()
    {
        RelativePath.TryParse(null, out var path).Should().BeFalse();
        path.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void TryParse_LeadingSlash_ReturnsFalse()
    {
        RelativePath.TryParse("/foo", out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_Valid_ReturnsTrue()
    {
        RelativePath.TryParse("../foo", out var path).Should().BeTrue();
        path.SegmentCount.Should().Be(2);
    }

    [Fact]
    public void Segments_OnDefault_Throws()
    {
        var path = default(RelativePath);

        Action act = () => _ = path.Segments;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IsCurrent_TrueForEmpty()
    {
        RelativePath.Current.IsCurrent.Should().BeTrue();
        RelativePath.Parse("foo").IsCurrent.Should().BeFalse();
    }

    [Fact]
    public void DotSegments_AreDropped()
    {
        var path = RelativePath.Parse("./foo/./bar/.");

        path.SegmentCount.Should().Be(2);
        path.Segments[0].Should().Be("foo");
        path.Segments[1].Should().Be("bar");
    }

    [Fact]
    public void DotDotSegments_ArePreserved()
    {
        var path = RelativePath.Parse("../foo/../bar");

        path.SegmentCount.Should().Be(4);
        path.Segments[0].Should().Be("..");
        path.Segments[1].Should().Be("foo");
        path.Segments[2].Should().Be("..");
        path.Segments[3].Should().Be("bar");
    }

    [Fact]
    public void ResolveAgainst_FsPath_Descends()
    {
        var resolved = RelativePath.Parse("b/c").ResolveAgainst(FsPath.Parse("/a"));

        resolved.Should().Be(FsPath.Parse("/a/b/c"));
    }

    [Fact]
    public void ResolveAgainst_FsPath_Ascends()
    {
        var resolved = RelativePath.Parse("../../x").ResolveAgainst(FsPath.Parse("/a/b/c"));

        resolved.Should().Be(FsPath.Parse("/a/x"));
    }

    [Fact]
    public void ResolveAgainst_FsPath_ClampsAtRoot()
    {
        var resolved = RelativePath.Parse("../../../../foo").ResolveAgainst(FsPath.Parse("/a"));

        resolved.Should().Be(FsPath.Parse("/foo"));
    }

    [Fact]
    public void ResolveAgainst_FsPath_ClampsToRootEntirely()
    {
        var resolved = RelativePath.Parse("../../..").ResolveAgainst(FsPath.Parse("/a/b"));

        resolved.Should().Be(FsPath.Root);
    }

    [Fact]
    public void ResolveAgainst_FsPath_Current_ReturnsBase()
    {
        var basePath = FsPath.Parse("/a/b");

        var resolved = RelativePath.Current.ResolveAgainst(basePath);

        resolved.Should().Be(basePath);
    }

    [Fact]
    public void ResolveAgainst_VfsUri_PreservesScheme()
    {
        var baseUri = VfsUri.Parse("mem:///a/b");

        var resolved = RelativePath.Parse("../c").ResolveAgainst(baseUri);

        resolved.Should().Be(VfsUri.Parse("mem:///a/c"));
        resolved.Scheme.Value.Should().Be("mem");
    }

    [Fact]
    public void ResolveAgainst_VfsUri_ClampsAtRoot()
    {
        var baseUri = VfsUri.Parse("file:///a");

        var resolved = RelativePath.Parse("../../../x").ResolveAgainst(baseUri);

        resolved.Should().Be(VfsUri.Parse("file:///x"));
    }

    [Fact]
    public void Equality_ComparesBySegments()
    {
        var a = RelativePath.Parse("foo/../bar");
        var b = RelativePath.Parse("foo/../bar");
        var c = RelativePath.Parse("foo/bar");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.Should().NotBe(c);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_DotNormalization()
    {
        RelativePath.Parse("./foo").Should().Be(RelativePath.Parse("foo"));
        RelativePath.Parse("./.").Should().Be(RelativePath.Current);
    }

    [Fact]
    public void Default_NotEqualToCurrent()
    {
        var def = default(RelativePath);

        def.IsDefault.Should().BeTrue();
        RelativePath.Current.IsDefault.Should().BeFalse();
        def.Should().NotBe(RelativePath.Current);
    }

    [Fact]
    public void CompareTo_SegmentWiseOrdinal()
    {
        // "a/b" < "a-c" because first segments: "a" < "a-c".
        var a = RelativePath.Parse("a/b");
        var b = RelativePath.Parse("a-c");

        a.CompareTo(b).Should().BeNegative();
    }

    [Fact]
    public void CompareTo_ShorterIsLess_WhenPrefix()
    {
        RelativePath.Parse("a").CompareTo(RelativePath.Parse("a/b")).Should().BeNegative();
    }

    [Theory]
    [InlineData("foo", "foo")]
    [InlineData("foo/bar", "foo/bar")]
    [InlineData("../foo", "../foo")]
    [InlineData("../../a/b", "../../a/b")]
    public void ToString_RoundTrips(string input, string expected)
    {
        var path = RelativePath.Parse(input);

        path.ToString().Should().Be(expected);
        RelativePath.Parse(path.ToString()).Should().Be(path);
    }

    [Fact]
    public void ToString_Current_ReturnsDot()
    {
        RelativePath.Current.ToString().Should().Be(".");
    }

    [Fact]
    public void ToString_OnDefault_ReturnsEmpty()
    {
        default(RelativePath).ToString().Should().Be(string.Empty);
    }

    [Fact]
    public void FromSegments_Empty_ReturnsCurrent()
    {
        RelativePath.FromSegments(System.Array.Empty<string>()).Should().Be(RelativePath.Current);
    }
}
