using System;
using VinDust.SharpVfs.Abstractions;

namespace VinDust.SharpVfs.Core;

/// <summary>
/// The default <see cref="IMountDecoratorFactory"/>: wraps non-mountable file systems in
/// <see cref="MountedFileSystem"/> and returns mountable ones unchanged.
/// </summary>
public sealed class DefaultMountDecoratorFactory : IMountDecoratorFactory
{
    /// <summary>A shared singleton instance.</summary>
    public static readonly DefaultMountDecoratorFactory Instance = new();

    private DefaultMountDecoratorFactory()
    {
    }

    /// <inheritdoc />
    public IMountableFileSystem Decorate(IFileSystem inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        return inner as IMountableFileSystem ?? new MountedFileSystem(inner);
    }
}