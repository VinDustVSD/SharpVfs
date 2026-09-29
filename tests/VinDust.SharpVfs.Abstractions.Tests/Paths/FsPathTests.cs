using FluentAssertions;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;
using Xunit;

namespace VinDust.SharpVfs.Abstractions.Tests.Paths;

/// <summary>Tests for <see cref="FsPath"/>.</summary>
public sealed class FsPathTests
{
    [Theory]
    [InlineData("/", 0)]
    [InlineData("/foo", 1)]
    [InlineData("/foo/bar", 2)]
    [InlineData("/foo/bar/baz", 3)]
    public void Parse_AcceptsValidPaths(string input, int expectedSegments)
    {
        var path = FsPath.Parse(input);

        path.IsDefault.Should().BeFalse();
        path.SegmentCount.Should().Be(expectedSegments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("foo")]              // no leading slash
    [InlineData("foo/")]             // no leading slash, trailing slash
    [InlineData("/foo/")]            // trailing slash
    [InlineData("//foo")]            // double slash
    [InlineData("/foo//bar")]        // double slash
    [InlineData("/foo/./bar")]       // '.' segment
    [InlineData("/.")]               // '.' segment
    [InlineData("/..")]              // '..' segment
    [InlineData("/foo/../bar")]      // '..' segment
    [InlineData("/foo\x00")]         // NUL
    [InlineData("/foo\x1F")]         // control char
    [InlineData("/foo\x7F")]         // DEL
    public void Parse_RejectsInvalidPaths(string input)
    {
        var act = () => FsPath.Parse(input);

        act.Should().Throw<VfsInvalidPathException>();
    }

    [Fact]
    public void Parse_Null_Throws()
    {
        var act = () => FsPath.Parse(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Parse_Root_ReturnsRoot()
    {
        var path = FsPath.Parse("/");

        path.Should().Be(FsPath.Root);
        path.IsRoot.Should().BeTrue();
        path.SegmentCount.Should().Be(0);
    }

    [Fact]
    public void TryParse_ReturnsFalse_OnInvalidInput()
    {
        var ok = FsPath.TryParse("foo", out var path);

        ok.Should().BeFalse();
        path.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void TryParse_ReturnsTrue_OnValidInput()
    {
        var ok = FsPath.TryParse("/foo/bar", out var path);

        ok.Should().BeTrue();
        path.SegmentCount.Should().Be(2);
        path.Segments[0].Should().Be("foo");
        path.Segments[1].Should().Be("bar");
    }

    [Fact]
    public void TryParse_Null_ReturnsFalse()
    {
        FsPath.TryParse(null, out _).Should().BeFalse();
    }

    [Fact]
    public void Segments_OnDefault_Throws()
    {
        var path = default(FsPath);

        Action act = () => _ = path.Segments;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SegmentCount_OnDefault_Throws()
    {
        var path = default(FsPath);

        var act = () => path.SegmentCount;

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("/", null)]
    [InlineData("/foo", "/")]
    [InlineData("/foo/bar", "/foo")]
    [InlineData("/a/b/c", "/a/b")]
    public void GetParent_ReturnsExpected(string input, string? expected)
    {
        var path = FsPath.Parse(input);

        var parent = path.GetParent();

        if (expected is null)
        {
            parent.Should().BeNull();
        }
        else
        {
            parent.Should().Be(FsPath.Parse(expected));
        }
    }

    [Theory]
    [InlineData("/", null)]
    [InlineData("/foo", "foo")]
    [InlineData("/foo/bar", "bar")]
    public void GetFileName_ReturnsExpected(string input, string? expected)
    {
        var path = FsPath.Parse(input);

        path.GetFileName().Should().Be(expected);
    }

    [Fact]
    public void SubPath_ZeroCount_ReturnsRoot()
    {
        var path = FsPath.Parse("/a/b/c");

        path.SubPath(1, 0).Should().Be(FsPath.Root);
    }

    [Fact]
    public void SubPath_TakesSlice()
    {
        var path = FsPath.Parse("/a/b/c/d");

        path.SubPath(1, 2).Should().Be(FsPath.Parse("/b/c"));
        path.SubPath(0, 4).Should().Be(path);
    }

    [Fact]
    public void SubPath_OutOfRange_Throws()
    {
        var path = FsPath.Parse("/a/b");

        var act1 = () => path.SubPath(-1, 1);
        var act2 = () => path.SubPath(0, 3);
        var act3 = () => path.SubPath(3, 0);

        act1.Should().Throw<ArgumentOutOfRangeException>();
        act2.Should().Throw<ArgumentOutOfRangeException>();
        act3.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void StartsWith_ComparesBySegments()
    {
        var path = FsPath.Parse("/foo/bar");

        path.StartsWith(FsPath.Root).Should().BeTrue();
        path.StartsWith(FsPath.Parse("/foo")).Should().BeTrue();
        path.StartsWith(FsPath.Parse("/foo/bar")).Should().BeTrue();
        path.StartsWith(FsPath.Parse("/foo/bar/baz")).Should().BeFalse();
        path.StartsWith(FsPath.Parse("/fo")).Should().BeFalse();
        path.StartsWith(FsPath.Parse("/bar")).Should().BeFalse();
    }

    [Fact]
    public void StartsWith_DoesNotMatchByStringPrefix()
    {
        // "/foo" must not match "/foobar" by string prefix — segment comparison only.
        var path = FsPath.Parse("/foobar");

        path.StartsWith(FsPath.Parse("/foo")).Should().BeFalse();
    }

    [Fact]
    public void RelativeTo_EqualPaths_ReturnsCurrent()
    {
        var path = FsPath.Parse("/a/b/c");

        var rel = path.RelativeTo(path);

        rel.Should().Be(RelativePath.Current);
    }

    [Fact]
    public void RelativeTo_Descendant_ReturnsDownward()
    {
        var rel = FsPath.Parse("/a/b/c").RelativeTo(FsPath.Parse("/a"));

        rel.ToString().Should().Be("b/c");
    }

    [Fact]
    public void RelativeTo_Ancestor_ReturnsUpward()
    {
        var rel = FsPath.Parse("/a").RelativeTo(FsPath.Parse("/a/b/c"));

        rel.ToString().Should().Be("../..");
    }

    [Fact]
    public void RelativeTo_CommonAncestor_ReturnsUpThenDown()
    {
        var rel = FsPath.Parse("/a/x/y").RelativeTo(FsPath.Parse("/a/b"));

        rel.ToString().Should().Be("../x/y");
    }

    [Fact]
    public void RelativeTo_UnrelatedSubtree_ReturnsUpThenDown()
    {
        var rel = FsPath.Parse("/x/y").RelativeTo(FsPath.Parse("/a/b"));

        rel.ToString().Should().Be("../../x/y");
    }

    [Fact]
    public void RelativeTo_FromRoot_ReturnsFullPath()
    {
        var rel = FsPath.Parse("/a/b").RelativeTo(FsPath.Root);

        rel.ToString().Should().Be("a/b");
    }

    [Fact]
    public void Equality_ComparesBySegments()
    {
        var a = FsPath.Parse("/foo/bar");
        var b = FsPath.Parse("/foo/bar");
        var c = FsPath.Parse("/foo/baz");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.Should().NotBe(c);
        (a != c).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Default_EqualsDefault()
    {
        var a = default(FsPath);
        var b = default(FsPath);

        a.Should().Be(b);
        a.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void CompareTo_UsesSegmentWiseOrdinalOrder()
    {
        // Segment-wise: "/a/b" < "/a-c" because "a" < "a-c" as first segments.
        // String-wise ordinal would give "/a-c" < "/a/b" ("-" 0x2D < "/" 0x2F).
        var a = FsPath.Parse("/a/b");
        var b = FsPath.Parse("/a-c");

        a.CompareTo(b).Should().BeNegative();
        (a < b).Should().BeTrue();
        (b > a).Should().BeTrue();
    }

    [Fact]
    public void CompareTo_ShorterIsLess_WhenPrefix()
    {
        var a = FsPath.Parse("/a");
        var b = FsPath.Parse("/a/b");

        a.CompareTo(b).Should().BeNegative();
    }

    [Fact]
    public void CompareTo_RootIsSmallest()
    {
        var root = FsPath.Root;
        var path = FsPath.Parse("/a");

        root.CompareTo(path).Should().BeNegative();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/foo")]
    [InlineData("/foo/bar")]
    [InlineData("/foo/bar/baz.qux")]
    public void ToString_RoundTrips(string input)
    {
        var path = FsPath.Parse(input);

        path.ToString().Should().Be(input);
        FsPath.Parse(path.ToString()).Should().Be(path);
    }

    [Fact]
    public void ToString_OnDefault_ReturnsEmpty()
    {
        default(FsPath).ToString().Should().Be(string.Empty);
    }

    [Fact]
    public void FromSegments_Empty_ReturnsRoot()
    {
        FsPath path = FsPath.FromSegments(System.Array.Empty<string>());

        path.Should().Be(FsPath.Root);
    }
}
