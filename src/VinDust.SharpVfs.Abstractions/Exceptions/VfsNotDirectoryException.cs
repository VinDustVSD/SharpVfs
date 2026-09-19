namespace VinDust.SharpVfs.Abstractions.Exceptions;

/// <summary>Thrown when an operation requires a directory but the target is not one.</summary>
public sealed class VfsNotDirectoryException : VfsException
{
    /// <summary>Initializes a new instance of the <see cref="VfsNotDirectoryException"/> class.</summary>
    public VfsNotDirectoryException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VfsNotDirectoryException"/> class with a specified error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public VfsNotDirectoryException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VfsNotDirectoryException"/> class with a specified error message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public VfsNotDirectoryException(string message, System.Exception innerException)
        : base(message, innerException)
    {
    }
}
