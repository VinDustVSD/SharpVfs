using FluentAssertions;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.Core.Tests.Fakes;

namespace VinDust.SharpVfs.Core.Tests;

/// <summary>Tests for <see cref="PathResolver"/>.</summary>
public sealed class PathResolverTests
{
    [Fact]
    public async Task Resolve_PlainFileSystem_ReturnsItself()
    {
        var fs = new RecordingFileSystem("plain");
        var resolver = new PathResolver();

        var resolved = await resolver.ResolveAsync(fs, FsPath.Parse("/a/b"));

        resolved.FileSystem.Should().BeSameAs(fs);
        resolved.Path.Should().Be(FsPath.Parse("/a/b"));
        resolved.ReadOnly.Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_OneMount_Redirects()
    {
        var inner = new RecordingFileSystem("inner");
        var target = new RecordingFileSystem("target");
        var mounted = new MountedFileSystem(inner);
        await mounted.MountAsync(FsPath.Parse("/m"), target, MountOptions.Default);

        var resolver = new PathResolver();
        var resolved = await resolver.ResolveAsync(mounted, FsPath.Parse("/m/sub/file"));

        resolved.FileSystem.Should().BeSameAs(target);
        resolved.Path.Should().Be(FsPath.Parse("/sub/file"));
    }

    [Fact]
    public async Task Resolve_LongestPrefixWins()
    {
        var inner = new RecordingFileSystem("inner");
        var shallow = new RecordingFileSystem("shallow");
        var deep = new RecordingFileSystem("deep");
        var mounted = new MountedFileSystem(inner);
        await mounted.MountAsync(FsPath.Parse("/a"), shallow, MountOptions.Default);
        await mounted.MountAsync(FsPath.Parse("/a/b"), deep, MountOptions.Default);

        var resolver = new PathResolver();
        var resolved = await resolver.ResolveAsync(mounted, FsPath.Parse("/a/b/c"));

        resolved.FileSystem.Should().BeSameAs(deep);
        resolved.Path.Should().Be(FsPath.Parse("/c"));
    }

    [Fact]
    public async Task Resolve_NestedMount_ChainsThrough()
    {
        var inner = new RecordingFileSystem("inner");
        var middle = new RecordingFileSystem("middle");
        var leaf = new RecordingFileSystem("leaf");

        var middleMounted = new MountedFileSystem(middle);
        await middleMounted.MountAsync(FsPath.Parse("/sub"), leaf, MountOptions.Default);

        var outer = new MountedFileSystem(inner);
        await outer.MountAsync(FsPath.Parse("/m"), middleMounted, MountOptions.Default);

        var resolver = new PathResolver();
        var resolved = await resolver.ResolveAsync(outer, FsPath.Parse("/m/sub/file"));

        resolved.FileSystem.Should().BeSameAs(leaf);
        resolved.Path.Should().Be(FsPath.Parse("/file"));
    }

    [Fact]
    public async Task Resolve_ReadOnlyMount_ReportsFlag()
    {
        var inner = new RecordingFileSystem("inner");
        var target = new RecordingFileSystem("target");
        var mounted = new MountedFileSystem(inner);
        await mounted.MountAsync(
            FsPath.Parse("/m"),
            target,
            new MountOptions { ReadOnly = true });

        var resolver = new PathResolver();
        var resolved = await resolver.ResolveAsync(mounted, FsPath.Parse("/m/file"));

        resolved.ReadOnly.Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_NoMountAtPath_ReturnsOwnerFs()
    {
        var inner = new RecordingFileSystem("inner");
        var target = new RecordingFileSystem("target");
        var mounted = new MountedFileSystem(inner);
        await mounted.MountAsync(FsPath.Parse("/m"), target, MountOptions.Default);

        var resolver = new PathResolver();
        var resolved = await resolver.ResolveAsync(mounted, FsPath.Parse("/other/file"));

        resolved.FileSystem.Should().BeSameAs(mounted);
        resolved.Path.Should().Be(FsPath.Parse("/other/file"));
    }

    [Fact]
    public async Task Resolve_Cycle_Throws()
    {
        var innerA = new RecordingFileSystem("a");
        var innerB = new RecordingFileSystem("b");
        var fsA = new MountedFileSystem(innerA);
        var fsB = new MountedFileSystem(innerB);

        await fsA.MountAsync(FsPath.Parse("/toB"), fsB, MountOptions.Default);
        await fsB.MountAsync(FsPath.Parse("/toA"), fsA, MountOptions.Default);

        var resolver = new PathResolver();
        var act = async () => await resolver.ResolveAsync(fsA, FsPath.Parse("/toB/toA/x"));

        await act.Should().ThrowAsync<VfsMountCycleException>();
    }

    [Fact]
    public async Task Resolve_NullStart_Throws()
    {
        var resolver = new PathResolver();
        var act = async () => await resolver.ResolveAsync(null!, FsPath.Parse("/a"));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Resolve_DefaultPath_Throws()
    {
        var fs = new RecordingFileSystem("plain");
        var resolver = new PathResolver();
        var act = async () => await resolver.ResolveAsync(fs, default);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}