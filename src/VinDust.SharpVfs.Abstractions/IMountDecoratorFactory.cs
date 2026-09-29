namespace VinDust.SharpVfs.Abstractions;

/// <summary>Creates mountable wrappers around file systems that do not implement mount capability directly.</summary>
public interface IMountDecoratorFactory
{
    /// <summary>Returns a mountable view of the specified file system.</summary>
    /// <param name="inner">The file system to wrap.</param>
    /// <returns>
    /// <paramref name="inner"/> itself when it is already mountable; otherwise a wrapper that adds mount capability.
    /// </returns>
    IMountableFileSystem Decorate(IFileSystem inner);
}