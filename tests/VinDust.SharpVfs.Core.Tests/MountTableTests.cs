using FluentAssertions;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.Core.Tests.Fakes;

namespace VinDust.SharpVfs.Core.Tests;

/// <summary>Tests for <see cref="MountTable"/>.</summary>
public sealed class MountTableTests
{
    private static (FakeFileSystem Owner, MountTable Table) CreateTable()
    {
        var owner = new FakeFileSystem("owner");
        return (owner, new MountTable(owner));
    }

    [Fact]
    public async Task Mount_StoresPoint()
    {
        var (_, table) = CreateTable();
        var target = new FakeFileSystem("target");

        var point = await table.MountAsync(FsPath.Parse("/m"), target, MountOptions.Default);

        point.Path.Should().Be(FsPath.Parse("/m"));
        point.Target.Should().BeSameAs(target);
        table.Count.Should().Be(1);
    }

    [Fact]
    public async Task Mount_TwiceAtDifferentPaths_RefcountsTarget()
    {
        var (_, table) = CreateTable();
        var target = new FakeFileSystem("target");

        await table.MountAsync(FsPath.Parse("/a"), target, MountOptions.Default);
        await table.MountAsync(FsPath.Parse("/b"), target, MountOptions.Default);

        table.Count.Should().Be(2);
        target.Disposed.Should().BeFalse();

        await table.UnmountAsync(FsPath.Parse("/a"));
        target.Disposed.Should().BeFalse();

        await table.UnmountAsync(FsPath.Parse("/b"));
        target.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Mount_ReplaceAtSamePath_DisposesOldTargetWhenUnreferenced()
    {
        var (_, table) = CreateTable();
        var first = new FakeFileSystem("first");
        var second = new FakeFileSystem("second");

        await table.MountAsync(FsPath.Parse("/m"), first, MountOptions.Default);
        await table.MountAsync(FsPath.Parse("/m"), second, MountOptions.Default);

        first.Disposed.Should().BeTrue();
        second.Disposed.Should().BeFalse();
        table.Count.Should().Be(1);
    }

    [Fact]
    public async Task Mount_SelfMount_Throws()
    {
        var (owner, table) = CreateTable();

        var act = async () => await table.MountAsync(FsPath.Parse("/m"), owner, MountOptions.Default);

        await act.Should().ThrowAsync<VfsMountCycleException>();
    }

    [Fact]
    public async Task Unmount_Missing_Throws()
    {
        var (_, table) = CreateTable();

        var act = async () => await table.UnmountAsync(FsPath.Parse("/missing"));

        await act.Should().ThrowAsync<VfsNotFoundException>();
    }

    [Fact]
    public async Task Unmount_DisposesTarget()
    {
        var (_, table) = CreateTable();
        var target = new FakeFileSystem("target");

        await table.MountAsync(FsPath.Parse("/m"), target, MountOptions.Default);
        await table.UnmountAsync(FsPath.Parse("/m"));

        target.Disposed.Should().BeTrue();
        table.Count.Should().Be(0);
    }

    [Fact]
    public async Task TryResolveMount_FindsLongestPrefix()
    {
        var (_, table) = CreateTable();
        var shallow = new FakeFileSystem("shallow");
        var deep = new FakeFileSystem("deep");

        await table.MountAsync(FsPath.Parse("/a"), shallow, MountOptions.Default);
        await table.MountAsync(FsPath.Parse("/a/b"), deep, MountOptions.Default);

        var point = table.TryResolveMount(FsPath.Parse("/a/b/c/d"), out var remaining);

        point.Should().NotBeNull();
        point!.Target.Should().BeSameAs(deep);
        point.Path.Should().Be(FsPath.Parse("/a/b"));
        remaining.Should().Be(FsPath.Parse("/c/d"));
    }

    [Fact]
    public async Task TryResolveMount_ExactMatch_EmptyRemaining()
    {
        var (_, table) = CreateTable();
        var target = new FakeFileSystem("target");

        await table.MountAsync(FsPath.Parse("/a/b"), target, MountOptions.Default);

        var point = table.TryResolveMount(FsPath.Parse("/a/b"), out var remaining);

        point.Should().NotBeNull();
        remaining.Should().Be(FsPath.Root);
    }

    [Fact]
    public void TryResolveMount_NoMatch_ReturnsNull()
    {
        var (_, table) = CreateTable();

        var point = table.TryResolveMount(FsPath.Parse("/a"), out _);

        point.Should().BeNull();
    }

    [Fact]
    public async Task GetMounts_ReturnsSnapshot()
    {
        var (_, table) = CreateTable();
        var a = new FakeFileSystem("a");
        var b = new FakeFileSystem("b");

        await table.MountAsync(FsPath.Parse("/b"), b, MountOptions.Default);
        await table.MountAsync(FsPath.Parse("/a"), a, MountOptions.Default);

        var mounts = table.GetMounts();

        mounts.Should().HaveCount(2);
        mounts.Select(m => m.Path.ToString()).Should().ContainInOrder("/a", "/b");
    }

    [Fact]
    public async Task Changed_RaisedOnAdd()
    {
        var (_, table) = CreateTable();
        var events = new List<MountTableChangedEventArgs>();
        table.Changed += (_, e) => events.Add(e);

        var target = new FakeFileSystem("target");
        await table.MountAsync(FsPath.Parse("/m"), target, MountOptions.Default);

        events.Should().ContainSingle();
        events[0].Kind.Should().Be(MountChangeKind.Added);
        events[0].Point.Path.Should().Be(FsPath.Parse("/m"));
    }

    [Fact]
    public async Task Changed_RaisedOnUnmount()
    {
        var (_, table) = CreateTable();
        var target = new FakeFileSystem("target");
        await table.MountAsync(FsPath.Parse("/m"), target, MountOptions.Default);

        var events = new List<MountTableChangedEventArgs>();
        table.Changed += (_, e) => events.Add(e);
        await table.UnmountAsync(FsPath.Parse("/m"));

        events.Should().ContainSingle();
        events[0].Kind.Should().Be(MountChangeKind.Removed);
    }

    [Fact]
    public async Task Changed_RaisedOnReplace()
    {
        var (_, table) = CreateTable();
        var first = new FakeFileSystem("first");
        var second = new FakeFileSystem("second");
        await table.MountAsync(FsPath.Parse("/m"), first, MountOptions.Default);

        var events = new List<MountTableChangedEventArgs>();
        table.Changed += (_, e) => events.Add(e);
        await table.MountAsync(FsPath.Parse("/m"), second, MountOptions.Default);

        events.Should().ContainSingle();
        events[0].Kind.Should().Be(MountChangeKind.Replaced);
        events[0].Point.Target.Should().BeSameAs(second);
        events[0].PreviousPoint!.Target.Should().BeSameAs(first);
    }

    [Fact]
    public void Constructor_NullOwner_Throws()
    {
        var act = () => new MountTable(null!);

        act.Should().Throw<System.ArgumentNullException>();
    }

    [Fact]
    public async Task Mount_DefaultPath_Throws()
    {
        var (_, table) = CreateTable();

        var act = async () => await table.MountAsync(default, new FakeFileSystem("t"), MountOptions.Default);

        await act.Should().ThrowAsync<System.ArgumentException>();
    }

    [Fact]
    public async Task Mount_NullTarget_Throws()
    {
        var (_, table) = CreateTable();

        var act = async () => await table.MountAsync(FsPath.Parse("/m"), null!, MountOptions.Default);

        await act.Should().ThrowAsync<System.ArgumentNullException>();
    }
}
