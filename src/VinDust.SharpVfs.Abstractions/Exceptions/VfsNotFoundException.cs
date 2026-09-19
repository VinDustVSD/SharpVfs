namespace VinDust.SharpVfs.Abstractions.Exceptions;

/// <summary>Thrown when an entry or mount point is expected to exist but is not found.</summary>
public sealed class VfsNotFoundException : VfsException
{
    /// <summary>Initializes a new instance of the <see cref="VfsNotFoundException"/> class.</summary>
    public VfsNotFoundException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VfsNotFoundException"/> class with a specified error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public VfsNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VfsNotFoundException"/> class with a specified error message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public VfsNotFoundException(string message, System.Exception innerException)
        : base(message, innerException)
    {
    }
}
