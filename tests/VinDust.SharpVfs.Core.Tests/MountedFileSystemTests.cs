using FluentAssertions;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.Core.Tests.Fakes;

namespace VinDust.SharpVfs.Core.Tests;

/// <summary>Tests for <see cref="MountedFileSystem"/>.</summary>
public sealed class MountedFileSystemTests
{
    [Fact]
    public async Task GetEntry_NoMount_DelegatesToInner()
    {
        var inner = new RecordingFileSystem("inner", "/a/file");
        var mounted = new MountedFileSystem(inner);

        var entry = await mounted.GetEntryAsync(FsPath.Parse("/a/file"));

        entry.Should().NotBeNull();
        inner.Calls.Should().ContainSingle(c => c.Op == "GetEntry" && c.Path == FsPath.Parse("/a/file"));
    }

    [Fact]
    public async Task GetEntry_UnderMount_DelegatesToTarget()
    {
        var inner = new RecordingFileSystem("inner");
        var target = new RecordingFileSystem("target", "/file");
        var mounted = new MountedFileSystem(inner);
        await mounted.MountAsync(FsPath.Parse("/m"), target, MountOptions.Default);

        var entry = await mounted.GetEntryAsync(FsPath.Parse("/m/file"));

        entry.Should().NotBeNull();
        target.Calls.Should().ContainSingle(c => c.Op == "GetEntry" && c.Path == FsPath.Parse("/file"));
        inner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenWrite_ReadOnlyMount_Throws()
    {
        var inner = new RecordingFileSystem("inner");
        var target = new RecordingFileSystem("target");
        var mounted = new MountedFileSystem(inner);
        await mounted.MountAsync(
            FsPath.Parse("/m"),
            target,
            new MountOptions { ReadOnly = true });

        var act = async () => await mounted.OpenWriteAsync(FsPath.Parse("/m/file"));

        await act.Should().ThrowAsync<VfsReadOnlyException>();
    }

    [Fact]
    public async Task OpenRead_ReadOnlyMount_Succeeds()
    {
        var inner = new RecordingFileSystem("inner");
        var target = new RecordingFileSystem("target");
        var mounted = new MountedFileSystem(inner);
        await mounted.MountAsync(
            FsPath.Parse("/m"),
            target,
            new MountOptions { ReadOnly = true });

        var stream = await mounted.OpenReadAsync(FsPath.Parse("/m/file"));

        stream.Should().NotBeNull();
        target.Calls.Should().ContainSingle(c => c.Op == "OpenRead" && c.Path == FsPath.Parse("/file"));
    }

    [Fact]
    public async Task Move_AcrossMounts_Throws()
    {
        var inner = new RecordingFileSystem("inner");
        var a = new RecordingFileSystem("a");
        var b = new RecordingFileSystem("b");
        var mounted = new MountedFileSystem(inner);
        await mounted.MountAsync(FsPath.Parse("/a"), a, MountOptions.Default);
        await mounted.MountAsync(FsPath.Parse("/b"), b, MountOptions.Default);

        var act = async () => await mounted.MoveAsync(
            FsPath.Parse("/a/file"),
            FsPath.Parse("/b/file"));

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Move_SameFileSystem_Delegates()
    {
        var inner = new RecordingFileSystem("inner");
        var a = new RecordingFileSystem("a");
        var mounted = new MountedFileSystem(inner);
        await mounted.MountAsync(FsPath.Parse("/m"), a, MountOptions.Default);

        await mounted.MoveAsync(
            FsPath.Parse("/m/src"),
            FsPath.Parse("/m/dst"));

        a.Calls.Should().Contain(c => c.Op == "Move" && c.Path == FsPath.Parse("/src"));
        a.Calls.Should().Contain(c => c.Op == "Move" && c.Path == FsPath.Parse("/dst"));
    }

    [Fact]
    public void TryResolveMount_NoMount_ReturnsNull()
    {
        var inner = new RecordingFileSystem("inner");
        var mounted = new MountedFileSystem(inner);

        var result = mounted.TryResolveMount(FsPath.Parse("/x"), out _);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryResolveMount_Match_ReturnsPoint()
    {
        var inner = new RecordingFileSystem("inner");
        var target = new RecordingFileSystem("target");
        var mounted = new MountedFileSystem(inner);
        await mounted.MountAsync(FsPath.Parse("/m"), target, MountOptions.Default);

        var result = mounted.TryResolveMount(FsPath.Parse("/m/sub"), out var remaining);

        result.Should().NotBeNull();
        result!.Path.Should().Be(FsPath.Parse("/m"));
        result.Target.Should().BeSameAs(target);
        remaining.Should().Be(FsPath.Parse("/sub"));
    }

    [Fact]
    public async Task DisposeAsync_DisposesInnerAndTargets()
    {
        var inner = new RecordingFileSystem("inner");
        var target = new RecordingFileSystem("target");
        var mounted = new MountedFileSystem(inner);
        await mounted.MountAsync(FsPath.Parse("/m"), target, MountOptions.Default);

        await mounted.DisposeAsync();

        var act = async () => await mounted.GetEntryAsync(FsPath.Parse("/x"));
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void DefaultMountDecoratorFactory_WrapsNonMountable()
    {
        var inner = new RecordingFileSystem("inner");

        var decorated = DefaultMountDecoratorFactory.Instance.Decorate(inner);

        decorated.Should().BeOfType<MountedFileSystem>();
    }

    [Fact]
    public void DefaultMountDecoratorFactory_ReturnsMountableAsIs()
    {
        var inner = new RecordingFileSystem("inner");
        var already = new MountedFileSystem(inner);

        var decorated = DefaultMountDecoratorFactory.Instance.Decorate(already);

        decorated.Should().BeSameAs(already);
    }
}