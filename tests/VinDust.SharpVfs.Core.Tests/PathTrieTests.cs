using FluentAssertions;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core.Tests;

/// <summary>Tests for <see cref="PathTrie{TValue}"/>.</summary>
public sealed class PathTrieTests
{
    [Fact]
    public void EmptyTrie_TryResolve_ReturnsFalse()
    {
        var trie = new PathTrie<string>();

        var ok = trie.TryResolve(FsPath.Parse("/a/b"), out var value, out var remaining);

        ok.Should().BeFalse();
        value.Should().BeNull();
        remaining.Should().Be(FsPath.Parse("/a/b"));
    }

    [Fact]
    public void Set_Then_TryGet_ExactMatch()
    {
        var trie = new PathTrie<string>();
        trie.Set(FsPath.Parse("/a/b"), "value");

        trie.TryGet(FsPath.Parse("/a/b"), out var value).Should().BeTrue();
        value.Should().Be("value");
        trie.TryGet(FsPath.Parse("/a"), out _).Should().BeFalse();
        trie.TryGet(FsPath.Parse("/a/b/c"), out _).Should().BeFalse();
    }

    [Fact]
    public void TryResolve_AtRoot()
    {
        var trie = new PathTrie<string>();
        trie.Set(FsPath.Root, "root");

        trie.TryResolve(FsPath.Parse("/a/b"), out var value, out var remaining).Should().BeTrue();
        value.Should().Be("root");
        remaining.Should().Be(FsPath.Parse("/a/b"));
    }

    [Fact]
    public void TryResolve_LongestPrefixWins()
    {
        var trie = new PathTrie<string>();
        trie.Set(FsPath.Parse("/a"), "shallow");
        trie.Set(FsPath.Parse("/a/b"), "deep");

        trie.TryResolve(FsPath.Parse("/a/b/c/d"), out var value, out var remaining).Should().BeTrue();
        value.Should().Be("deep");
        remaining.Should().Be(FsPath.Parse("/c/d"));
    }

    [Fact]
    public void TryResolve_ExactMatch_HasEmptyRemaining()
    {
        var trie = new PathTrie<string>();
        trie.Set(FsPath.Parse("/a/b"), "value");

        trie.TryResolve(FsPath.Parse("/a/b"), out _, out var remaining).Should().BeTrue();
        remaining.Should().Be(FsPath.Root);
    }

    [Fact]
    public void TryResolve_DoesNotStopAtFirstMatch()
    {
        // This is the critical test: the resolver must not break out on the first
        // value found. It must continue descending to find a deeper match.
        var trie = new PathTrie<string>();
        trie.Set(FsPath.Parse("/a"), "shallow");
        trie.Set(FsPath.Parse("/a/b/c/d/e"), "deepest");

        trie.TryResolve(FsPath.Parse("/a/b/c/d/e/f"), out var value, out var remaining).Should().BeTrue();
        value.Should().Be("deepest");
        remaining.Should().Be(FsPath.Parse("/f"));
    }

    [Fact]
    public void TryResolve_NoMatchingPrefix_ReturnsFalse()
    {
        var trie = new PathTrie<string>();
        trie.Set(FsPath.Parse("/a"), "value");

        trie.TryResolve(FsPath.Parse("/b/c"), out var value, out _).Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void Set_ReplacesExisting_ReturnsTrue()
    {
        var trie = new PathTrie<string>();
        trie.Set(FsPath.Parse("/a"), "first").Should().BeFalse();
        trie.Set(FsPath.Parse("/a"), "second").Should().BeTrue();

        trie.TryGet(FsPath.Parse("/a"), out var value).Should().BeTrue();
        value.Should().Be("second");
        trie.Count.Should().Be(1);
    }

    [Fact]
    public void Remove_ReturnsTrueWhenRemoved()
    {
        var trie = new PathTrie<string>();
        trie.Set(FsPath.Parse("/a/b"), "value");

        trie.Remove(FsPath.Parse("/a/b")).Should().BeTrue();
        trie.TryGet(FsPath.Parse("/a/b"), out _).Should().BeFalse();
        trie.Count.Should().Be(0);
    }

    [Fact]
    public void Remove_ReturnsFalseWhenMissing()
    {
        var trie = new PathTrie<string>();
        trie.Set(FsPath.Parse("/a"), "value");

        trie.Remove(FsPath.Parse("/a/b")).Should().BeFalse();
        trie.Remove(FsPath.Parse("/x")).Should().BeFalse();
        trie.Count.Should().Be(1);
    }

    [Fact]
    public void Remove_CleansUpEmptyParents()
    {
        var trie = new PathTrie<string>();
        trie.Set(FsPath.Parse("/a/b/c"), "value");
        trie.Remove(FsPath.Parse("/a/b/c")).Should().BeTrue();

        // Re-resolve must not match anything.
        trie.TryResolve(FsPath.Parse("/a/b/c/d"), out _, out _).Should().BeFalse();

        // Verify internals through Enumerate — should be empty.
        trie.Enumerate().Should().BeEmpty();
    }

    [Fact]
    public void Remove_PreservesSiblingBranch()
    {
        var trie = new PathTrie<string>();
        trie.Set(FsPath.Parse("/a/b"), "b-value");
        trie.Set(FsPath.Parse("/a/c"), "c-value");

        trie.Remove(FsPath.Parse("/a/b")).Should().BeTrue();

        trie.TryGet(FsPath.Parse("/a/c"), out var value).Should().BeTrue();
        value.Should().Be("c-value");
        trie.Count.Should().Be(1);
    }

    [Fact]
    public void Remove_ValueNode_KeepsChildren()
    {
        var trie = new PathTrie<string>();
        trie.Set(FsPath.Parse("/a"), "a-value");
        trie.Set(FsPath.Parse("/a/b"), "b-value");

        trie.Remove(FsPath.Parse("/a")).Should().BeTrue();

        // The node /a should still exist because it has a child.
        trie.TryResolve(FsPath.Parse("/a/b"), out var value, out _).Should().BeTrue();
        value.Should().Be("b-value");
        trie.Count.Should().Be(1);
    }

    [Fact]
    public void Enumerate_ReturnsAllValues()
    {
        var trie = new PathTrie<string>();
        trie.Set(FsPath.Parse("/a"), "1");
        trie.Set(FsPath.Parse("/a/b"), "2");
        trie.Set(FsPath.Parse("/c/d/e"), "3");

        var entries = trie.Enumerate().ToList();

        entries.Should().HaveCount(3);
        entries.Should().ContainEquivalentOf(new KeyValuePair<FsPath, string>(FsPath.Parse("/a"), "1"));
        entries.Should().ContainEquivalentOf(new KeyValuePair<FsPath, string>(FsPath.Parse("/a/b"), "2"));
        entries.Should().ContainEquivalentOf(new KeyValuePair<FsPath, string>(FsPath.Parse("/c/d/e"), "3"));
    }

    [Fact]
    public void Enumerate_EmptyTrie_ReturnsEmpty()
    {
        var trie = new PathTrie<string>();

        trie.Enumerate().Should().BeEmpty();
    }

    [Fact]
    public void Count_TracksAddsAndRemoves()
    {
        var trie = new PathTrie<string>();

        trie.Count.Should().Be(0);
        trie.Set(FsPath.Parse("/a"), "1");
        trie.Set(FsPath.Parse("/b"), "2");
        trie.Set(FsPath.Parse("/c"), "3");
        trie.Count.Should().Be(3);
        trie.Set(FsPath.Parse("/a"), "replaced");
        trie.Count.Should().Be(3);
        trie.Remove(FsPath.Parse("/a"));
        trie.Count.Should().Be(2);
        trie.Remove(FsPath.Parse("/a"));
        trie.Count.Should().Be(2);
    }

    [Fact]
    public void Version_IncreasesOnMutation()
    {
        var trie = new PathTrie<string>();
        var v0 = trie.Version;

        trie.Set(FsPath.Parse("/a"), "1");
        var v1 = trie.Version;
        v1.Should().BeGreaterThan(v0);

        trie.Set(FsPath.Parse("/a"), "2");     // replacement still bumps version
        var v2 = trie.Version;
        v2.Should().BeGreaterThan(v1);

        trie.Remove(FsPath.Parse("/a"));
        var v3 = trie.Version;
        v3.Should().BeGreaterThan(v2);

        trie.Remove(FsPath.Parse("/a"));       // no-op
        trie.Version.Should().Be(v3);
    }

    [Fact]
    public void TryGet_DefaultPath_Throws()
    {
        var trie = new PathTrie<string>();

        var act = () => trie.TryGet(default, out _);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryResolve_DefaultPath_Throws()
    {
        var trie = new PathTrie<string>();

        var act = () => trie.TryResolve(default, out _, out _);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Set_DefaultPath_Throws()
    {
        var trie = new PathTrie<string>();

        var act = () => trie.Set(default, "value");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Set_NullValue_Throws()
    {
        var trie = new PathTrie<string>();

        var act = () => trie.Set(FsPath.Parse("/a"), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Remove_DefaultPath_Throws()
    {
        var trie = new PathTrie<string>();

        var act = () => trie.Remove(default);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_DoNotThrow()
    {
        // Smoke test for RCU correctness under contention.
        var trie = new PathTrie<string>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var writers = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
        {
            var n = 0;
            while (!cts.IsCancellationRequested)
            {
                trie.Set(FsPath.Parse($"/w{w}/k{n % 50}"), $"v{n}");
                trie.Remove(FsPath.Parse($"/w{w}/k{(n + 25) % 50}"));
                n++;
            }
        })).ToArray();

        var readers = Enumerable.Range(0, 4).Select(r => Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                trie.TryResolve(FsPath.Parse("/w0/k1/sub"), out _, out _);
                _ = trie.Enumerate().Take(10).ToList();
                _ = trie.Count;
            }
        })).ToArray();

        await Task.WhenAll(writers.Concat(readers));
    }
}