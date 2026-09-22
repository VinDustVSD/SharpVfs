using FluentAssertions;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.Core;
using VinDust.SharpVfs.Core.Tests.Fakes;
using Xunit;

namespace VinDust.SharpVfs.Core.Tests;

public sealed class VfsChainResolverTests
{
    private static FsPath P(string s) => FsPath.Parse(s);

    [Fact]
    public void Resolve_NoMounts_ReturnsStart()
    {
        var fs = new RecordingFileSystem("plain");

        var r = VfsChainResolver.Resolve(fs, P("/a/b"));

        r.FileSystem.Should().BeSameAs(fs);
        r.Path.Should().Be(P("/a/b"));
        r.CrossedMounts.Should().BeFalse();
        r.ReadOnly.Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_OneMount_RecordsChain()
    {
        var inner = new RecordingFileSystem("inner");
        var target = new RecordingFileSystem("target");
        var mounted = new MountedFileSystem(inner);
        await mounted.MountAsync(P("/m"), target, MountOptions.Default);

        var r = VfsChainResolver.Resolve(mounted, P("/m/sub/file"));

        r.FileSystem.Should().BeSameAs(target);
        r.Path.Should().Be(P("/sub/file"));
        r.CrossedMounts.Should().BeTrue();
        r.MountChain.Should().ContainSingle();
        r.MountChain[0].Path.Should().Be(P("/m"));
        r.MountChain[0].Target.Should().BeSameAs(target);
    }

    [Fact]
    public async Task Resolve_NestedMounts_ChainHasBoth()
    {
        var inner = new RecordingFileSystem("inner");
        var middle = new RecordingFileSystem("middle");
        var leaf = new RecordingFileSystem("leaf");

        var middleMounted = new MountedFileSystem(middle);
        await middleMounted.MountAsync(P("/sub"), leaf, MountOptions.Default);

        var outer = new MountedFileSystem(inner);
        await outer.MountAsync(P("/a"), middleMounted, MountOptions.Default);

        var r = VfsChainResolver.Resolve(outer, P("/a/sub/file"));

        r.FileSystem.Should().BeSameAs(leaf);
        r.Path.Should().Be(P("/file"));
        r.MountChain.Should().HaveCount(2);
    }

    [Fact]
    public async Task Resolve_ReadOnlyMount_SetsFlag()
    {
        var inner = new RecordingFileSystem("inner");
        var target = new RecordingFileSystem("target");
        var mounted = new MountedFileSystem(inner);
        await mounted.MountAsync(P("/m"), target, new MountOptions { ReadOnly = true });

        var r = VfsChainResolver.Resolve(mounted, P("/m/x"));

        r.ReadOnly.Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_Cycle_Throws()
    {
        var innerA = new RecordingFileSystem("a");
        var innerB = new RecordingFileSystem("b");
        var fsA = new MountedFileSystem(innerA);
        var fsB = new MountedFileSystem(innerB);

        await fsA.MountAsync(P("/toB"), fsB, MountOptions.Default);
        await fsB.MountAsync(P("/toA"), fsA, MountOptions.Default);

        var act = () => VfsChainResolver.Resolve(fsA, P("/toB/toA/x"));

        act.Should().Throw<Abstractions.Exceptions.VfsMountCycleException>();
    }
}