namespace VinDust.SharpVfs.Abstractions.Exceptions;

/// <summary>Thrown when a write operation is attempted against a read-only file system or mount.</summary>
public sealed class VfsReadOnlyException : VfsException
{
    /// <summary>Initializes a new instance of the <see cref="VfsReadOnlyException"/> class.</summary>
    public VfsReadOnlyException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VfsReadOnlyException"/> class with a specified error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public VfsReadOnlyException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VfsReadOnlyException"/> class with a specified error message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public VfsReadOnlyException(string message, System.Exception innerException)
        : base(message, innerException)
    {
    }
}
