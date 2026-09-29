namespace VinDust.SharpVfs.Abstractions.Exceptions;

/// <summary>
/// Base type for all exceptions thrown by the SharpVfs library.
/// </summary>
/// <remarks>
/// <para>
/// All VFS-specific error conditions are represented by exceptions derived from
/// <see cref="VfsException"/>. Library code does not throw <see cref="System.IO.IOException"/>
/// or its derivatives across its public API surface.
/// </para>
/// <para>
/// <see cref="OperationCanceledException"/> is the sole exception to this rule:
/// cancellation is signaled via the standard .NET cancellation mechanism.
/// </para>
/// </remarks>
public class VfsException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="VfsException"/> class.</summary>
    public VfsException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VfsException"/> class with a specified error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public VfsException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VfsException"/> class with a specified error message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public VfsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
