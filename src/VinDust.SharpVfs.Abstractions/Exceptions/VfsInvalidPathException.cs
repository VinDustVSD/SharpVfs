using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Abstractions.Exceptions;

/// <summary>
/// Thrown when a string cannot be parsed into a valid <see cref="VfsUri"/>,
/// <see cref="FsPath"/>, or <see cref="RelativePath"/>.
/// </summary>
public sealed class VfsInvalidPathException : VfsException
{
    /// <summary>Initializes a new instance of the <see cref="VfsInvalidPathException"/> class.</summary>
    public VfsInvalidPathException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VfsInvalidPathException"/> class with a specified error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public VfsInvalidPathException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VfsInvalidPathException"/> class with a specified error message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public VfsInvalidPathException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
