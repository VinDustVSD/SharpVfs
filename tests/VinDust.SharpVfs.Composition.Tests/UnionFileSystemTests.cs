using FluentAssertions;
using System.Text;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.Abstractions.Policies;
using VinDust.SharpVfs.FileSystems.Memory;
using Xunit;

namespace VinDust.SharpVfs.Composition.Tests;

public sealed class UnionFileSystemTests
{
    private static FsPath P(string s) => FsPath.Parse(s);

    private static async Task<MemoryFileSystem> CreateFsAsync(params (string Path, string Content)[] files)
    {
        var fs = new MemoryFileSystem(new MemoryFileSystemOptions { CreateParentOnWrite = true });
        foreach (var (path, content) in files)
        {
            await using var w = await fs.OpenWriteAsync(P(path));
            await w.WriteAsync(Encoding.UTF8.GetBytes(content));
        }

        return fs;
    }

    private static async Task<string> ReadAllAsync(IFileSystem fs, FsPath path)
    {
        await using var r = await fs.OpenReadAsync(path);
        using var reader = new StreamReader(r);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public void Constructor_EmptyLayers_Throws()
    {
        var act = () => new UnionFileSystem([]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullLayer_Throws()
    {
        var act = () => new UnionFileSystem([new MemoryFileSystem(), null!]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task GetEntry_TopWins()
    {
        var top = await CreateFsAsync(("/file.txt", "top"));
        var bottom = await CreateFsAsync(("/file.txt", "bottom"));
        await using var union = new UnionFileSystem([top, bottom]);

        var entry = await union.GetEntryAsync(P("/file.txt"));

        entry.Should().NotBeNull();
        (await ReadAllAsync(union, P("/file.txt"))).Should().Be("top");
    }

    [Fact]
    public async Task GetEntry_FallsThroughToLowerLayer()
    {
        var top = await CreateFsAsync(("/only-top.txt", "t"));
        var bottom = await CreateFsAsync(("/only-bottom.txt", "b"));
        await using var union = new UnionFileSystem([top, bottom]);

        (await union.ExistsAsync(P("/only-bottom.txt"))).Should().BeTrue();

        var entry = await union.GetEntryAsync(P("/only-bottom.txt"));
        entry.Should().NotBeNull();
        (await ReadAllAsync(union, P("/only-bottom.txt"))).Should().Be("b");
    }

    [Fact]
    public async Task GetEntry_Missing_ReturnsNull()
    {
        var top = await CreateFsAsync();
        var bottom = await CreateFsAsync();
        await using var union = new UnionFileSystem([top, bottom]);

        (await union.GetEntryAsync(P("/missing"))).Should().BeNull();
    }

    [Fact]
    public async Task Enumerate_MergesAllLayers()
    {
        var top = await CreateFsAsync(("/dir/a.txt", "a"), ("/dir/conflict.txt", "top"));
        var middle = await CreateFsAsync(("/dir/b.txt", "b"), ("/dir/conflict.txt", "middle"));
        var bottom = await CreateFsAsync(("/dir/c.txt", "c"));
        await using var union = new UnionFileSystem([top, middle, bottom]);

        var names = new System.Collections.Generic.List<string>();
        await foreach (var entry in union.EnumerateAsync(P("/dir")))
        {
            names.Add(entry.Path.GetFileName()!);
        }

        names.Should().BeEquivalentTo(["a.txt", "b.txt", "c.txt", "conflict.txt"]);
    }

    [Fact]
    public async Task Enumerate_NotDirectory_Throws()
    {
        var top = await CreateFsAsync(("/file.txt", "x"));
        var bottom = await CreateFsAsync();
        await using var union = new UnionFileSystem([top, bottom]);

        var act = async () =>
        {
            await foreach (var _ in union.EnumerateAsync(P("/file.txt"))) { }
        };

        await act.Should().ThrowAsync<VfsNotDirectoryException>();
    }

    [Fact]
    public async Task OpenWrite_GoesToTopWritableLayer()
    {
        var top = await CreateFsAsync();
        var bottom = await CreateFsAsync();
        await using var union = new UnionFileSystem([top, bottom]);

        await using (var w = await union.OpenWriteAsync(P("/new.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("x"));
        }

        (await top.ExistsAsync(P("/new.txt"))).Should().BeTrue();
        (await bottom.ExistsAsync(P("/new.txt"))).Should().BeFalse();
    }

    [Fact]
    public async Task OpenWrite_NoWritableLayer_Throws()
    {
        var readOnly = new ReadOnlyFileSystem(await CreateFsAsync());
        await using var union = new UnionFileSystem([readOnly]);

        var act = async () => await union.OpenWriteAsync(P("/x"));

        await act.Should().ThrowAsync<VfsReadOnlyException>();
    }

    [Fact]
    public async Task OpenWrite_CustomPolicy_SelectsLayer()
    {
        var top = await CreateFsAsync();
        var bottom = await CreateFsAsync();

        // Always pick the bottom layer.
        var policy = new LambdaPolicy(ctx => ctx.Candidates[ctx.Candidates.Count - 1]);
        await using var union = new UnionFileSystem([top, bottom], policy);

        await using (var w = await union.OpenWriteAsync(P("/f.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("x"));
        }

        (await top.ExistsAsync(P("/f.txt"))).Should().BeFalse();
        (await bottom.ExistsAsync(P("/f.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task Delete_RemovesFromAllWritableLayers()
    {
        var top = await CreateFsAsync(("/f.txt", "top"));
        var bottom = await CreateFsAsync(("/f.txt", "bottom"));
        await using var union = new UnionFileSystem([top, bottom]);

        await union.DeleteAsync(P("/f.txt"));

        (await top.ExistsAsync(P("/f.txt"))).Should().BeFalse();
        (await bottom.ExistsAsync(P("/f.txt"))).Should().BeFalse();
        (await union.ExistsAsync(P("/f.txt"))).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_ReadOnlyLayer_Skipped()
    {
        var top = await CreateFsAsync();
        var bottom = new ReadOnlyFileSystem(await CreateFsAsync(("/f.txt", "b")));
        await using var union = new UnionFileSystem([top, bottom]);

        await union.DeleteAsync(P("/f.txt"));

        // Read-only bottom still has it — no whiteouts in v1.
        (await union.ExistsAsync(P("/f.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task Move_SameLayer_Works()
    {
        var top = await CreateFsAsync(("/a.txt", "x"));
        var bottom = await CreateFsAsync();
        await using var union = new UnionFileSystem([top, bottom]);

        await union.MoveAsync(P("/a.txt"), P("/b.txt"));

        (await top.ExistsAsync(P("/a.txt"))).Should().BeFalse();
        (await top.ExistsAsync(P("/b.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task Move_CrossLayer_Throws()
    {
        var top = await CreateFsAsync();
        var bottom = await CreateFsAsync(("/a.txt", "x"));
        await using var union = new UnionFileSystem([top, bottom]);

        var act = async () => await union.MoveAsync(P("/a.txt"), P("/b.txt"));

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Copy_SameLayer_Delegates()
    {
        var top = await CreateFsAsync(("/a.txt", "x"));
        var bottom = await CreateFsAsync();
        await using var union = new UnionFileSystem([top, bottom]);

        await union.CopyAsync(P("/a.txt"), P("/b.txt"));

        (await top.ExistsAsync(P("/a.txt"))).Should().BeTrue();
        (await top.ExistsAsync(P("/b.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task Copy_CrossLayer_CopiesViaStreams()
    {
        var top = await CreateFsAsync();
        var bottom = await CreateFsAsync(("/a.txt", "from-bottom"));
        await using var union = new UnionFileSystem([top, bottom]);

        await union.CopyAsync(P("/a.txt"), P("/b.txt"));

        // Source stays in bottom; destination lands in top (default policy).
        (await bottom.ExistsAsync(P("/a.txt"))).Should().BeTrue();
        (await top.ExistsAsync(P("/b.txt"))).Should().BeTrue();
        (await ReadAllAsync(top, P("/b.txt"))).Should().Be("from-bottom");
    }

    [Fact]
    public async Task Capabilities_AllReadOnly_NoWriteFlag()
    {
        var ro1 = new ReadOnlyFileSystem(await CreateFsAsync());
        var ro2 = new ReadOnlyFileSystem(await CreateFsAsync());
        await using var union = new UnionFileSystem([ro1, ro2]);

        union.Capabilities.Should().HaveFlag(FileSystemCapabilities.Read);
        union.Capabilities.Should().NotHaveFlag(FileSystemCapabilities.Write);
    }

    [Fact]
    public async Task Capabilities_OneWritable_HasWriteFlag()
    {
        var writable = await CreateFsAsync();
        var ro = new ReadOnlyFileSystem(await CreateFsAsync());
        await using var union = new UnionFileSystem([writable, ro]);

        union.Capabilities.Should().HaveFlag(FileSystemCapabilities.Write);
    }

    [Fact]
    public async Task DisposeAsync_DisposesLayers()
    {
        var top = await CreateFsAsync();
        var bottom = await CreateFsAsync();
        var union = new UnionFileSystem([top, bottom]);

        await union.DisposeAsync();

        var topAct = async () => await top.ExistsAsync(P("/"));
        var bottomAct = async () => await bottom.ExistsAsync(P("/"));
        await topAct.Should().ThrowAsync<ObjectDisposedException>();
        await bottomAct.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task UseAfterDispose_Throws()
    {
        var top = await CreateFsAsync();
        var union = new UnionFileSystem([top]);
        await union.DisposeAsync();

        var act = async () => await union.ExistsAsync(P("/x"));

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>Test-only policy that runs a lambda against the context.</summary>
    private sealed class LambdaPolicy : IWritePolicy
    {
        private readonly Func<WriteContext, IFileSystem> _selector;

        public LambdaPolicy(Func<WriteContext, IFileSystem> selector)
        {
            _selector = selector;
        }

        public ValueTask<IFileSystem> SelectAsync(WriteContext context, CancellationToken ct = default)
            => new(_selector(context));
    }
}