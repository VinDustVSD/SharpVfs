using FluentAssertions;
using System.Text;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.FileSystems.Memory;
using Xunit;

namespace VinDust.SharpVfs.Composition.Tests;

public sealed class ReadOnlyFileSystemTests
{
    private static FsPath P(string s) => FsPath.Parse(s);

    private static async Task<(MemoryFileSystem Inner, ReadOnlyFileSystem Ro)> CreateWithFileAsync(string content = "hello")
    {
        var inner = new MemoryFileSystem(new MemoryFileSystemOptions { CreateParentOnWrite = true });
        await using (var w = await inner.OpenWriteAsync(P("/file.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes(content));
        }

        return (inner, new ReadOnlyFileSystem(inner));
    }

    [Fact]
    public async Task Exists_DelegatesToInner()
    {
        var (_, ro) = await CreateWithFileAsync();

        (await ro.ExistsAsync(P("/file.txt"))).Should().BeTrue();
        (await ro.ExistsAsync(P("/missing"))).Should().BeFalse();
    }

    [Fact]
    public async Task GetEntry_DelegatesToInner()
    {
        var (_, ro) = await CreateWithFileAsync();

        var entry = await ro.GetEntryAsync(P("/file.txt"));

        entry.Should().NotBeNull();
        entry!.Size.Should().Be(5);
    }

    [Fact]
    public async Task OpenRead_DelegatesToInner()
    {
        var (_, ro) = await CreateWithFileAsync("readonly");

        await using var r = await ro.OpenReadAsync(P("/file.txt"));
        using var reader = new StreamReader(r);

        (await reader.ReadToEndAsync()).Should().Be("readonly");
    }

    [Fact]
    public async Task OpenWrite_Throws()
    {
        var (_, ro) = await CreateWithFileAsync();

        var act = async () => await ro.OpenWriteAsync(P("/new.txt"));

        await act.Should().ThrowAsync<VfsReadOnlyException>();
    }

    [Fact]
    public async Task CreateDirectory_Throws()
    {
        var (_, ro) = await CreateWithFileAsync();

        var act = async () => await ro.CreateDirectoryAsync(P("/dir"));

        await act.Should().ThrowAsync<VfsReadOnlyException>();
    }

    [Fact]
    public async Task Delete_Throws()
    {
        var (_, ro) = await CreateWithFileAsync();

        var act = async () => await ro.DeleteAsync(P("/file.txt"));

        await act.Should().ThrowAsync<VfsReadOnlyException>();
    }

    [Fact]
    public async Task Move_Throws()
    {
        var (_, ro) = await CreateWithFileAsync();

        var act = async () => await ro.MoveAsync(P("/file.txt"), P("/other.txt"));

        await act.Should().ThrowAsync<VfsReadOnlyException>();
    }

    [Fact]
    public async Task Copy_Throws()
    {
        var (_, ro) = await CreateWithFileAsync();

        var act = async () => await ro.CopyAsync(P("/file.txt"), P("/other.txt"));

        await act.Should().ThrowAsync<VfsReadOnlyException>();
    }

    [Fact]
    public async Task CreateSymbolicLink_Throws()
    {
        var (_, ro) = await CreateWithFileAsync();

        var act = async () => await ro.CreateSymbolicLinkAsync(P("/link"), RelativePath.Parse("file.txt"));

        await act.Should().ThrowAsync<VfsReadOnlyException>();
    }

    [Fact]
    public async Task Capabilities_DropWriteFlags()
    {
        var (_, ro) = await CreateWithFileAsync();

        ro.Capabilities.Should().HaveFlag(FileSystemCapabilities.Read);
        ro.Capabilities.Should().NotHaveFlag(FileSystemCapabilities.Write);
        ro.Capabilities.Should().NotHaveFlag(FileSystemCapabilities.CreateDirectory);
        ro.Capabilities.Should().NotHaveFlag(FileSystemCapabilities.Delete);
        ro.Capabilities.Should().NotHaveFlag(FileSystemCapabilities.Move);
        ro.Capabilities.Should().NotHaveFlag(FileSystemCapabilities.Copy);
        ro.Capabilities.Should().NotHaveFlag(FileSystemCapabilities.Symlinks);
    }

    [Fact]
    public async Task DisposeAsync_DisposesInner()
    {
        var (inner, ro) = await CreateWithFileAsync();

        await ro.DisposeAsync();

        var act = async () => await inner.ExistsAsync(P("/file.txt"));
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task UseAfterDispose_Throws()
    {
        var (_, ro) = await CreateWithFileAsync();
        await ro.DisposeAsync();

        var act = async () => await ro.ExistsAsync(P("/file.txt"));

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Enumerate_DelegatesToInner()
    {
        var inner = new MemoryFileSystem(new MemoryFileSystemOptions { CreateParentOnWrite = true });
        await using (var w = await inner.OpenWriteAsync(P("/a.txt"))) { }
        await using (var w = await inner.OpenWriteAsync(P("/b.txt"))) { }
        var ro = new ReadOnlyFileSystem(inner);

        var names = new System.Collections.Generic.List<string>();
        await foreach (var entry in ro.EnumerateAsync(P("/")))
        {
            names.Add(entry.Path.GetFileName()!);
        }

        names.Should().BeEquivalentTo(["a.txt", "b.txt"]);
    }
}