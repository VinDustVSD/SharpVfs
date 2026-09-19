using FluentAssertions;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;
using Xunit;

namespace VinDust.SharpVfs.Abstractions.Tests.Paths;

/// <summary>Tests for <see cref="VfsScheme"/>.</summary>
public sealed class VfsSchemeTests
{
    [Theory]
    [InlineData("mem")]
    [InlineData("file")]
    [InlineData("zip")]
    [InlineData("a")]
    [InlineData("a1")]
    [InlineData("a+b")]
    [InlineData("a-b")]
    [InlineData("a.b")]
    [InlineData("z9+-.")]
    public void Parse_AcceptsValidSchemes(string input)
    {
        var scheme = VfsScheme.Parse(input);

        scheme.IsDefault.Should().BeFalse();
        scheme.Value.Should().Be(input);
    }

    [Theory]
    [InlineData("MEM", "mem")]
    [InlineData("File", "file")]
    [InlineData("Zip", "zip")]
    [InlineData("MEMORY+FILE", "memory+file")]
    public void Parse_FoldsUppercaseToLowercase(string input, string expected)
    {
        var scheme = VfsScheme.Parse(input);

        scheme.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1mem")]
    [InlineData("+mem")]
    [InlineData("-mem")]
    [InlineData(".mem")]
    [InlineData("me m")]
    [InlineData("me/m")]
    [InlineData("me:m")]
    [InlineData("me_m")]
    [InlineData("мем")]
    [InlineData("mem\n")]
    public void Parse_RejectsInvalidSchemes(string input)
    {
        var act = () => VfsScheme.Parse(input);

        act.Should().Throw<VfsInvalidPathException>();
    }

    [Fact]
    public void Parse_Null_Throws()
    {
        var act = () => VfsScheme.Parse(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1bad")]
    [InlineData("bad space")]
    public void TryParse_ReturnsFalse_OnInvalidInput(string? input)
    {
        var ok = VfsScheme.TryParse(input, out var scheme);

        ok.Should().BeFalse();
        scheme.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void TryParse_ReturnsTrue_OnValidInput()
    {
        var ok = VfsScheme.TryParse("MEM", out var scheme);

        ok.Should().BeTrue();
        scheme.Value.Should().Be("mem");
    }

    [Fact]
    public void Value_OnDefault_Throws()
    {
        var scheme = default(VfsScheme);

        var act = () => scheme.Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Equality_ComparesByValue()
    {
        var a = VfsScheme.Parse("mem");
        var b = VfsScheme.Parse("mem");
        var c = VfsScheme.Parse("file");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.Should().NotBe(c);
        (a != c).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_MatchesForEqualInstances()
    {
        var a = VfsScheme.Parse("mem");
        var b = VfsScheme.Parse("mem");

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Default_EqualsDefault()
    {
        var a = default(VfsScheme);
        var b = default(VfsScheme);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void CompareTo_OrdersLexicographically()
    {
        var file = VfsScheme.Parse("file");
        var mem = VfsScheme.Parse("mem");
        var zip = VfsScheme.Parse("zip");

        file.CompareTo(mem).Should().BeNegative();
        mem.CompareTo(zip).Should().BeNegative();
        zip.CompareTo(mem).Should().BePositive();
        mem.CompareTo(mem).Should().Be(0);
        (file < mem).Should().BeTrue();
        (zip > mem).Should().BeTrue();
    }

    [Fact]
    public void ToString_ReturnsCanonicalForm()
    {
        VfsScheme.Parse("MEM").ToString().Should().Be("mem");
        default(VfsScheme).ToString().Should().Be(string.Empty);
    }

    [Fact]
    public void TryParse_Span_Overload_Works()
    {
        var ok = VfsScheme.TryParse("ZIP".AsSpan(), out var scheme);

        ok.Should().BeTrue();
        scheme.Value.Should().Be("zip");
    }
}
