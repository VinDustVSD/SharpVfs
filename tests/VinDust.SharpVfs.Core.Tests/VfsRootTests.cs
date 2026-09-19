using FluentAssertions;
using System.Runtime.CompilerServices;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.Core.Tests.Fakes;

namespace VinDust.SharpVfs.Core.Tests;

/// <summary>Tests for <see cref="VfsRoot"/>.</summary>
public sealed class VfsRootTests
{
    private static VfsRoot CreateRoot(
        string scheme,
        IFileSystem fs,
        IContentHashProvider? hashProvider = null)
    {
        var registry = new DefaultSchemeRegistry();
        registry.Register(VfsScheme.Parse(scheme), fs);
        return new VfsRoot(registry, hashProvider: hashProvider);
    }

    [Fact]
    public async Task GetEntry_RegisteredScheme_Delegates()
    {
        var fs = new RecordingFileSystem("mem", "/a/file");
        var root = CreateRoot("mem", fs);

        var entry = await root.GetEntryAsync(VfsUri.Parse("mem:///a/file"));

        entry.Should().NotBeNull();
        entry!.Uri.Should().Be(VfsUri.Parse("mem:///a/file"));
        entry.Path.Should().Be(FsPath.Parse("/a/file"));
        entry.IsFile.Should().BeTrue();
        fs.Calls.Should().ContainSingle(c => c.Op == "GetEntry" && c.Path == FsPath.Parse("/a/file"));
    }

    [Fact]
    public async Task GetEntry_Missing_ReturnsNull()
    {
        var fs = new RecordingFileSystem("mem");
        var root = CreateRoot("mem", fs);

        var entry = await root.GetEntryAsync(VfsUri.Parse("mem:///missing"));

        entry.Should().BeNull();
    }

    [Fact]
    public async Task GetEntry_UnregisteredScheme_Throws()
    {
        var fs = new RecordingFileSystem("mem");
        var root = CreateRoot("mem", fs);

        var act = async () => await root.GetEntryAsync(VfsUri.Parse("other:///a"));

        await act.Should().ThrowAsync<VfsSchemeNotFoundException>();
    }

    [Fact]
    public async Task Exists_Registered_Works()
    {
        var fs = new RecordingFileSystem("mem", "/a");
        var root = CreateRoot("mem", fs);

        (await root.ExistsAsync(VfsUri.Parse("mem:///a"))).Should().BeTrue();
        (await root.ExistsAsync(VfsUri.Parse("mem:///b"))).Should().BeFalse();
    }

    [Fact]
    public async Task Mount_ThroughRoot_DispatchesToScheme()
    {
        var inner = new RecordingFileSystem("mem");
        var target = new RecordingFileSystem("target", "/file");
        var root = CreateRoot("mem", inner);

        await root.MountAsync(
            VfsUri.Parse("mem:///m"),
            target,
            MountOptions.Default);

        var entry = await root.GetEntryAsync(VfsUri.Parse("mem:///m/file"));

        entry.Should().NotBeNull();
        target.Calls.Should().ContainSingle(c => c.Op == "GetEntry" && c.Path == FsPath.Parse("/file"));
        inner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Mount_ThenUnmount_Detaches()
    {
        var inner = new RecordingFileSystem("mem", "/m");
        var target = new RecordingFileSystem("target", "/file");
        var root = CreateRoot("mem", inner);

        await root.MountAsync(VfsUri.Parse("mem:///m"), target, MountOptions.Default);
        await root.UnmountAsync(VfsUri.Parse("mem:///m"));

        var mounts = await root.GetMountsAsync(VfsUri.Parse("mem:///"));
        mounts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMounts_ReflectsCurrentState()
    {
        var inner = new RecordingFileSystem("mem");
        var target = new RecordingFileSystem("target");
        var root = CreateRoot("mem", inner);

        await root.MountAsync(VfsUri.Parse("mem:///m"), target, MountOptions.Default);

        var mounts = await root.GetMountsAsync(VfsUri.Parse("mem:///"));

        mounts.Should().ContainSingle();
        mounts.First().Path.Should().Be(FsPath.Parse("/m"));
        mounts.First().Target.Should().BeSameAs(target);
    }

    [Fact]
    public async Task OpenWrite_ReadOnlyMount_Throws()
    {
        var inner = new RecordingFileSystem("mem");
        var target = new RecordingFileSystem("target");
        var root = CreateRoot("mem", inner);

        await root.MountAsync(
            VfsUri.Parse("mem:///m"),
            target,
            new MountOptions { ReadOnly = true });

        var act = async () => await root.OpenWriteAsync(VfsUri.Parse("mem:///m/file"));

        await act.Should().ThrowAsync<VfsReadOnlyException>();
    }

    [Fact]
    public async Task OpenRead_ReadOnlyMount_Succeeds()
    {
        var inner = new RecordingFileSystem("mem");
        var target = new RecordingFileSystem("target");
        var root = CreateRoot("mem", inner);

        await root.MountAsync(
            VfsUri.Parse("mem:///m"),
            target,
            new MountOptions { ReadOnly = true });

        var stream = await root.OpenReadAsync(VfsUri.Parse("mem:///m/file"));

        stream.Should().NotBeNull();
        target.Calls.Should().ContainSingle(c => c.Op == "OpenRead" && c.Path == FsPath.Parse("/file"));
    }

    [Fact]
    public async Task GetContentHash_EagerHash_NoProviderCall()
    {
        var fs = new HashyFileSystem("/file", "crc32:deadbeef");
        var provider = new CountingHashProvider();
        var root = CreateRoot("mem", fs, provider);

        var hash = await root.GetContentHashAsync(VfsUri.Parse("mem:///file"));

        hash.Should().Be("crc32:deadbeef");
        provider.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetContentHash_NoEagerHash_UsesProvider()
    {
        var fs = new HashyFileSystem("/file", eagerHash: null);
        var provider = new CountingHashProvider("sha256:abcdef");
        var root = CreateRoot("mem", fs, provider);

        var hash = await root.GetContentHashAsync(VfsUri.Parse("mem:///file"));

        hash.Should().Be("sha256:abcdef");
        provider.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetContentHash_NoEagerHash_NoProvider_ReturnsNull()
    {
        var fs = new HashyFileSystem("/file", eagerHash: null);
        var root = CreateRoot("mem", fs);

        var hash = await root.GetContentHashAsync(VfsUri.Parse("mem:///file"));

        hash.Should().BeNull();
    }

    [Fact]
    public async Task Dispose_Twice_IsIdempotent()
    {
        var fs = new RecordingFileSystem("mem");
        var root = CreateRoot("mem", fs);

        await root.DisposeAsync();
        await root.DisposeAsync();

        var act = async () => await root.GetEntryAsync(VfsUri.Parse("mem:///a"));
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task GetEntry_AfterDispose_Throws()
    {
        var fs = new RecordingFileSystem("mem");
        var root = CreateRoot("mem", fs);
        await root.DisposeAsync();

        var act = async () => await root.GetEntryAsync(VfsUri.Parse("mem:///a"));

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    /// <summary>Fake FS that always returns a FileEntry with a fixed content hash.</summary>
    private sealed class HashyFileSystem : IFileSystem
    {
        private readonly FsPath _path;
        private readonly string? _hash;

        public HashyFileSystem(string path, string? eagerHash)
        {
            _path = FsPath.Parse(path);
            _hash = eagerHash;
        }

        public FileSystemCapabilities Capabilities => FileSystemCapabilities.Read;

        public ValueTask<bool> ExistsAsync(FsPath path, CancellationToken ct = default)
            => new(path == _path);

        public ValueTask<Abstractions.Entries.FileSystemEntry?> GetEntryAsync(FsPath path, CancellationToken ct = default)
            => new(path == _path ? new Abstractions.Entries.FileEntry(path) { ContentHash = _hash } : null);

        public async IAsyncEnumerable<Abstractions.Entries.FileSystemEntry> EnumerateAsync(
            FsPath path,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask<Stream> OpenReadAsync(FsPath path, CancellationToken ct = default)
            => new(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("content")));

        public ValueTask<Stream> OpenWriteAsync(FsPath path, FileWriteMode mode = FileWriteMode.Create, CancellationToken ct = default)
            => throw new NotSupportedException();

        public ValueTask CreateDirectoryAsync(FsPath path, CancellationToken ct = default)
            => throw new NotSupportedException();

        public ValueTask DeleteAsync(FsPath path, bool recursive = false, CancellationToken ct = default)
            => throw new NotSupportedException();

        public ValueTask MoveAsync(FsPath source, FsPath destination, CancellationToken ct = default)
            => throw new NotSupportedException();

        public ValueTask CopyAsync(FsPath source, FsPath destination, CancellationToken ct = default)
            => throw new NotSupportedException();

        public ValueTask CreateSymbolicLinkAsync(FsPath path, RelativePath target, CancellationToken ct = default)
            => throw new NotSupportedException();

        public ValueTask DisposeAsync() => default;
    }

    /// <summary>Provider that counts invocations.</summary>
    private sealed class CountingHashProvider : IContentHashProvider
    {
        private readonly string? _result;

        public CountingHashProvider(string? result = null)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public ValueTask<string?> ComputeAsync(Stream content, CancellationToken ct = default)
        {
            CallCount++;
            return new(_result);
        }
    }
}