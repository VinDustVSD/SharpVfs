namespace VinDust.SharpVfs.Abstractions.Exceptions;

/// <summary>Thrown when a URI references a scheme that has not been registered.</summary>
public sealed class VfsSchemeNotFoundException : VfsException
{
    /// <summary>Initializes a new instance of the <see cref="VfsSchemeNotFoundException"/> class.</summary>
    public VfsSchemeNotFoundException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VfsSchemeNotFoundException"/> class with a specified error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public VfsSchemeNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VfsSchemeNotFoundException"/> class with a specified error message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public VfsSchemeNotFoundException(string message, System.Exception innerException)
        : base(message, innerException)
    {
    }
}
