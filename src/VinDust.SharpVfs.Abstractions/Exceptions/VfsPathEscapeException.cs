namespace VinDust.SharpVfs.Abstractions.Exceptions;

/// <summary>Thrown when a resolved path escapes the boundary of its containing file system.</summary>
public sealed class VfsPathEscapeException : VfsException
{
    /// <summary>Initializes a new instance of the <see cref="VfsPathEscapeException"/> class.</summary>
    public VfsPathEscapeException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VfsPathEscapeException"/> class with a specified error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public VfsPathEscapeException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VfsPathEscapeException"/> class with a specified error message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public VfsPathEscapeException(string message, System.Exception innerException)
        : base(message, innerException)
    {
    }
}
