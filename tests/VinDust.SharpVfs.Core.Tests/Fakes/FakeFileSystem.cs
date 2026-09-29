using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core.Tests.Fakes;

/// <summary>Minimal <see cref="IFileSystem"/> stub for tests that don't need real I/O.</summary>
internal sealed class FakeFileSystem : IFileSystem
{
    private readonly string _name;

    public FakeFileSystem(string name)
    {
        _name = name;
    }

    public string Name => _name;

    public bool Disposed { get; private set; }

    public FileSystemCapabilities Capabilities => FileSystemCapabilities.Read;

    public ValueTask<bool> ExistsAsync(FsPath path, CancellationToken ct = default) => new(false);

    public ValueTask<FileSystemEntry?> GetEntryAsync(FsPath path, CancellationToken ct = default)
        => new((FileSystemEntry?)null);

    public IAsyncEnumerable<FileSystemEntry> EnumerateAsync(FsPath path, CancellationToken ct = default)
        => Empty();

    private static async IAsyncEnumerable<FileSystemEntry> Empty()
    {
        await Task.CompletedTask;
        yield break;
    }

    public ValueTask<Stream> OpenReadAsync(FsPath path, CancellationToken ct = default)
        => throw new NotSupportedException();

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

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return default;
    }

    public override string ToString() => _name;
}