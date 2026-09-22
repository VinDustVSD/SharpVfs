using FluentAssertions;
using System.IO.Compression;
using System.Text;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.FileSystems.Zip;

namespace VinDust.SharpVfs.FileSystems.Tests;

public sealed class ZipArchiveFileSystemTests
{
    private static FsPath P(string s) => FsPath.Parse(s);

    private static MemoryStream CreateZipStream(params (string Name, string Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var w = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                w.Write(bytes, 0, bytes.Length);
            }
        }

        ms.Position = 0;
        return ms;
    }

    private static async Task<string> ReadAllAsync(Stream s)
    {
        using var reader = new StreamReader(s);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task Exists_TopLevelFile_ReturnsTrue()
    {
        await using var fs = new ZipArchiveFileSystem(CreateZipStream(("readme.txt", "hi")));

        (await fs.ExistsAsync(P("/readme.txt"))).Should().BeTrue();
        (await fs.ExistsAsync(P("/missing.txt"))).Should().BeFalse();
    }

    [Fact]
    public async Task Exists_NestedFile_ReturnsTrue()
    {
        await using var fs = new ZipArchiveFileSystem(CreateZipStream(("dir/file.txt", "x")));

        (await fs.ExistsAsync(P("/dir/file.txt"))).Should().BeTrue();
        (await fs.ExistsAsync(P("/dir"))).Should().BeTrue();   // implicit directory
        (await fs.ExistsAsync(P("/"))).Should().BeTrue();
    }

    [Fact]
    public async Task GetEntry_File_HasMetadata()
    {
        await using var fs = new ZipArchiveFileSystem(CreateZipStream(("file.txt", "hello")));

        var entry = await fs.GetEntryAsync(P("/file.txt"));

        entry.Should().BeOfType<FileEntry>();
        entry!.Size.Should().Be(5);
        entry.ContentHash.Should().StartWith("crc32:");
    }

    [Fact]
    public async Task GetEntry_Directory_IsDirectoryEntry()
    {
        await using var fs = new ZipArchiveFileSystem(CreateZipStream(("dir/file.txt", "x")));

        var entry = await fs.GetEntryAsync(P("/dir"));

        entry.Should().BeOfType<DirectoryEntry>();
    }

    [Fact]
    public async Task GetEntry_Missing_ReturnsNull()
    {
        await using var fs = new ZipArchiveFileSystem(CreateZipStream(("file.txt", "x")));

        (await fs.GetEntryAsync(P("/missing"))).Should().BeNull();
    }

    [Fact]
    public async Task OpenRead_ReturnsContent()
    {
        await using var fs = new ZipArchiveFileSystem(CreateZipStream(("file.txt", "hello world")));

        await using var s = await fs.OpenReadAsync(P("/file.txt"));

        (await ReadAllAsync(s)).Should().Be("hello world");
    }

    [Fact]
    public async Task OpenRead_Missing_Throws()
    {
        await using var fs = new ZipArchiveFileSystem(CreateZipStream(("file.txt", "x")));

        var act = async () => await fs.OpenReadAsync(P("/missing"));

        await act.Should().ThrowAsync<VfsNotFoundException>();
    }

    [Fact]
    public async Task OpenRead_Directory_Throws()
    {
        await using var fs = new ZipArchiveFileSystem(CreateZipStream(("dir/file.txt", "x")));

        var act = async () => await fs.OpenReadAsync(P("/dir"));

        await act.Should().ThrowAsync<VfsNotDirectoryException>();
    }

    [Fact]
    public async Task Enumerate_Root_ReturnsTopLevelChildren()
    {
        await using var fs = new ZipArchiveFileSystem(CreateZipStream(
            ("readme.txt", "a"),
            ("dir/file.txt", "b"),
            ("dir/sub/other.txt", "c")));

        var names = new System.Collections.Generic.List<string>();
        await foreach (var entry in fs.EnumerateAsync(P("/")))
        {
            names.Add(entry.Path.GetFileName()!);
        }

        names.Should().BeEquivalentTo(["readme.txt", "dir"]);
    }

    [Fact]
    public async Task Enumerate_NestedDirectory()
    {
        await using var fs = new ZipArchiveFileSystem(CreateZipStream(
            ("dir/a.txt", "a"),
            ("dir/b.txt", "b"),
            ("dir/sub/c.txt", "c")));

        var names = new System.Collections.Generic.List<string>();
        await foreach (var entry in fs.EnumerateAsync(P("/dir")))
        {
            names.Add(entry.Path.GetFileName()!);
        }

        names.Should().BeEquivalentTo(["a.txt", "b.txt", "sub"]);
    }

    [Fact]
    public async Task Enumerate_MissingDirectory_Throws()
    {
        await using var fs = new ZipArchiveFileSystem(CreateZipStream(("file.txt", "x")));

        var act = async () =>
        {
            await foreach (var _ in fs.EnumerateAsync(P("/missing"))) { }
        };

        await act.Should().ThrowAsync<VfsNotDirectoryException>();
    }

    [Fact]
    public async Task Enumerate_File_Throws()
    {
        await using var fs = new ZipArchiveFileSystem(CreateZipStream(("file.txt", "x")));

        var act = async () =>
        {
            await foreach (var _ in fs.EnumerateAsync(P("/file.txt"))) { }
        };

        await act.Should().ThrowAsync<VfsNotDirectoryException>();
    }

    [Fact]
    public async Task ExplicitDirectoryEntry_UsesItsMetadata()
    {
        var ms = CreateZipStream(("dir/", ""), ("dir/file.txt", "x"));
        await using var fs = new ZipArchiveFileSystem(ms);

        var entry = await fs.GetEntryAsync(P("/dir"));

        entry.Should().BeOfType<DirectoryEntry>();
    }

    [Fact]
    public async Task OpenWrite_WritableStream_Succeeds()
    {
        var ms = CreateZipStream(("file.txt", "x"));
        await using var fs = new ZipArchiveFileSystem(ms, leaveOpen: true, ZipFileSystemOptions.Default);

        await using (var w = await fs.OpenWriteAsync(P("/new.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("y"));
        }

        (await fs.ExistsAsync(P("/new.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task Write_ReadOnlyStream_Throws()
    {
        var inner = CreateZipStream(("file.txt", "x"));
        var readOnly = new ReadOnlyStream(inner);
        await using var fs = new ZipArchiveFileSystem(readOnly, leaveOpen: true, ZipFileSystemOptions.Default);

        var act = async () => await fs.OpenWriteAsync(P("/new.txt"));

        await act.Should().ThrowAsync<VfsReadOnlyException>();
    }

    [Fact]
    public async Task Capabilities_WritableStream_HasWriteFlags()
    {
        var ms = CreateZipStream(("file.txt", "x"));
        await using var fs = new ZipArchiveFileSystem(ms, leaveOpen: true, ZipFileSystemOptions.Default);

        fs.Capabilities.Should().HaveFlag(Abstractions.FileSystemCapabilities.Read);
        fs.Capabilities.Should().HaveFlag(Abstractions.FileSystemCapabilities.Write);
        fs.Capabilities.Should().HaveFlag(Abstractions.FileSystemCapabilities.PatchableContainer);
    }

    [Fact]
    public async Task Capabilities_ReadOnlyStream_OnlyRead()
    {
        var inner = CreateZipStream(("file.txt", "x"));
        var readOnly = new ReadOnlyStream(inner);
        await using var fs = new ZipArchiveFileSystem(readOnly, leaveOpen: true, ZipFileSystemOptions.Default);

        fs.Capabilities.Should().Be(Abstractions.FileSystemCapabilities.Read | Abstractions.FileSystemCapabilities.Seekable);
    }

    [Theory]
    [InlineData("file.txt", "file.txt")]
    [InlineData("dir/file.txt", "dir/file.txt")]
    [InlineData("dir\\file.txt", "dir/file.txt")]      // backslash normalized
    [InlineData("dir/", "dir/")]
    [InlineData("a/b/c.txt", "a/b/c.txt")]
    public void NormalizeEntryName_Valid(string input, string expected)
    {
        ZipArchiveFileSystem.NormalizeEntryName(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/absolute.txt")]
    [InlineData("../escape.txt")]
    [InlineData("dir/../escape.txt")]
    [InlineData("dir/./file.txt")]
    [InlineData("dir//file.txt")]
    [InlineData("bad\x01char.txt")]
    public void NormalizeEntryName_Invalid(string input)
    {
        ZipArchiveFileSystem.NormalizeEntryName(input).Should().BeNull();
    }

    [Fact]
    public async Task GetRawEntryNames_ReturnsAllEntries()
    {
        await using var fs = new ZipArchiveFileSystem(CreateZipStream(
            ("valid.txt", "a"),
            ("dir/valid2.txt", "b")));

        var names = fs.GetRawEntryNames();

        names.Should().BeEquivalentTo(["valid.txt", "dir/valid2.txt"]);
    }

    [Fact]
    public async Task OpenRawEntry_ReturnsContent()
    {
        await using var fs = new ZipArchiveFileSystem(CreateZipStream(("file.txt", "raw content")));

        using var s = fs.OpenRawEntry("file.txt");

        (await ReadAllAsync(s)).Should().Be("raw content");
    }

    [Fact]
    public async Task OpenRawEntry_Missing_Throws()
    {
        await using var fs = new ZipArchiveFileSystem(CreateZipStream(("file.txt", "x")));

        var act = () => fs.OpenRawEntry("missing.txt");

        act.Should().Throw<VfsNotFoundException>();
    }

    [Fact]
    public async Task Constructor_InvalidZip_Throws()
    {
        var ms = new MemoryStream(Encoding.UTF8.GetBytes("not a zip file"));

        var act = () => new ZipArchiveFileSystem(ms);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task DisposeAsync_DisposesOwnedStream()
    {
        var ms = CreateZipStream(("file.txt", "x"));
        var fs = new ZipArchiveFileSystem(ms);

        await fs.DisposeAsync();

        ms.CanRead.Should().BeFalse();
    }

    [Fact]
    public async Task DisposeAsync_LeaveOpen_KeepsStreamOpen()
    {
        var ms = CreateZipStream(("file.txt", "x"));
        var fs = new ZipArchiveFileSystem(ms, leaveOpen: true, ZipFileSystemOptions.Default);

        await fs.DisposeAsync();

        ms.CanRead.Should().BeTrue();
    }

    [Fact]
    public async Task UseAfterDispose_Throws()
    {
        var fs = new ZipArchiveFileSystem(CreateZipStream(("file.txt", "x")));
        await fs.DisposeAsync();

        var act = async () => await fs.ExistsAsync(P("/file.txt"));

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Write_RequiresWritableStream()
    {
        var readOnly = CreateZipStream(("file.txt", "x"));   // MemoryStream is writable, так что не подходит
        // Используем read-only обёртку:
        var readOnlyStream = new ReadOnlyStream(readOnly);
        await using var fs = new ZipArchiveFileSystem(readOnlyStream, leaveOpen: true, ZipFileSystemOptions.Default);

        var act = async () => await fs.OpenWriteAsync(P("/new.txt"));

        await act.Should().ThrowAsync<VfsReadOnlyException>();
    }

    [Fact]
    public async Task Write_AddsFile_And_PersistsToStream()
    {
        var ms = CreateZipStream(("existing.txt", "old"));
        await using (var fs = new ZipArchiveFileSystem(ms, leaveOpen: true, ZipFileSystemOptions.Default))
        {
            await using var w = await fs.OpenWriteAsync(P("/new.txt"));
            await w.WriteAsync(Encoding.UTF8.GetBytes("new content"));
        }

        // Reopen and verify.
        ms.Position = 0;
        await using var fs2 = new ZipArchiveFileSystem(ms, leaveOpen: true, ZipFileSystemOptions.Default);

        (await fs2.ExistsAsync(P("/new.txt"))).Should().BeTrue();
        await using var s = await fs2.OpenReadAsync(P("/new.txt"));
        using var reader = new StreamReader(s);
        (await reader.ReadToEndAsync()).Should().Be("new content");

        // Original still exists.
        (await fs2.ExistsAsync(P("/existing.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task WriteSession_BatchesMultipleWrites()
    {
        var ms = CreateZipStream(("a.txt", "1"));
        await using var fs = new ZipArchiveFileSystem(ms, leaveOpen: true, ZipFileSystemOptions.Default);

        await using (var session = fs.BeginWriteSession())
        {
            await using (var w = await fs.OpenWriteAsync(P("/b.txt")))
            {
                await w.WriteAsync(Encoding.UTF8.GetBytes("2"));
            }
            await using (var w = await fs.OpenWriteAsync(P("/c.txt")))
            {
                await w.WriteAsync(Encoding.UTF8.GetBytes("3"));
            }

            // Inside the session, all writes are visible to reads.
            (await fs.ExistsAsync(P("/b.txt"))).Should().BeTrue();
            (await fs.ExistsAsync(P("/c.txt"))).Should().BeTrue();

            await session.CommitAsync();
        }

        ms.Position = 0;
        await using var fs2 = new ZipArchiveFileSystem(ms, leaveOpen: true, ZipFileSystemOptions.Default);

        (await fs2.ExistsAsync(P("/a.txt"))).Should().BeTrue();
        (await fs2.ExistsAsync(P("/b.txt"))).Should().BeTrue();
        (await fs2.ExistsAsync(P("/c.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task WriteSession_Rollback_DiscardsChanges()
    {
        var ms = CreateZipStream(("a.txt", "1"));
        await using var fs = new ZipArchiveFileSystem(ms, leaveOpen: true, ZipFileSystemOptions.Default);

        await using (var session = fs.BeginWriteSession())
        {
            await using (var w = await fs.OpenWriteAsync(P("/b.txt")))
            {
                await w.WriteAsync(Encoding.UTF8.GetBytes("2"));
            }

            session.Rollback();
        }

        (await fs.ExistsAsync(P("/b.txt"))).Should().BeFalse();
        (await fs.ExistsAsync(P("/a.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task WriteSession_CommitWithOpenStream_Throws()
    {
        var ms = CreateZipStream(("a.txt", "1"));
        await using var fs = new ZipArchiveFileSystem(ms, leaveOpen: true, ZipFileSystemOptions.Default);

        await using var session = fs.BeginWriteSession();
        var stream = await fs.OpenWriteAsync(P("/b.txt"));
        await stream.WriteAsync(Encoding.UTF8.GetBytes("x"));

        var act = async () => await session.CommitAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        await stream.DisposeAsync();
    }

    [Fact]
    public async Task WriteSession_Nested_Throws()
    {
        var ms = CreateZipStream(("a.txt", "1"));
        await using var fs = new ZipArchiveFileSystem(ms, leaveOpen: true, ZipFileSystemOptions.Default);

        await using var outer = fs.BeginWriteSession();

        var act = () => fs.BeginWriteSession();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task ReplaceEntry_ReplacesContent()
    {
        var ms = CreateZipStream(("file.txt", "old content"));
        await using var fs = new ZipArchiveFileSystem(ms, leaveOpen: true, ZipFileSystemOptions.Default);

        using var newContent = new MemoryStream(Encoding.UTF8.GetBytes("new content"));
        await ((IPatchableContainerFileSystem)fs).ReplaceEntryAsync(
            P("/file.txt"),
            newContent,
            ContainerPatchOptions.Default);

        await using var s = await fs.OpenReadAsync(P("/file.txt"));
        using var reader = new StreamReader(s);
        (await reader.ReadToEndAsync()).Should().Be("new content");
    }

    [Fact]
    public async Task ReplaceEntry_Missing_Throws()
    {
        var ms = CreateZipStream(("file.txt", "x"));
        await using var fs = new ZipArchiveFileSystem(ms, leaveOpen: true, ZipFileSystemOptions.Default);

        using var content = new MemoryStream(Encoding.UTF8.GetBytes("new"));
        var act = async () => await ((IPatchableContainerFileSystem)fs).ReplaceEntryAsync(
            P("/missing.txt"), content, ContainerPatchOptions.Default);

        await act.Should().ThrowAsync<VfsNotFoundException>();
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        var ms = CreateZipStream(("a.txt", "1"), ("b.txt", "2"));
        await using var fs = new ZipArchiveFileSystem(ms, leaveOpen: true, ZipFileSystemOptions.Default);

        await fs.DeleteAsync(P("/a.txt"));

        (await fs.ExistsAsync(P("/a.txt"))).Should().BeFalse();
        (await fs.ExistsAsync(P("/b.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task CreateDirectory_VisibleInEnumerate()
    {
        var ms = CreateZipStream(("a.txt", "1"));
        await using var fs = new ZipArchiveFileSystem(ms, leaveOpen: true, ZipFileSystemOptions.Default);

        await fs.CreateDirectoryAsync(P("/empty"));

        (await fs.ExistsAsync(P("/empty"))).Should().BeTrue();
        var entry = await fs.GetEntryAsync(P("/empty"));
        entry.Should().BeOfType<DirectoryEntry>();
    }

    /// <summary>Read-only wrapper over a stream.</summary>
    private sealed class ReadOnlyStream : Stream
    {
        private readonly Stream _inner;

        public ReadOnlyStream(Stream inner) { _inner = inner; }

        public override bool CanRead => true;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}