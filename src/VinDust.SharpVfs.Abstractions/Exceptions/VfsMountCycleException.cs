namespace VinDust.SharpVfs.Abstractions.Exceptions;

/// <summary>Thrown when a mount operation would introduce a cycle.</summary>
public sealed class VfsMountCycleException : VfsException
{
    /// <summary>Initializes a new instance of the <see cref="VfsMountCycleException"/> class.</summary>
    public VfsMountCycleException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VfsMountCycleException"/> class with a specified error message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public VfsMountCycleException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="VfsMountCycleException"/> class with a specified error message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public VfsMountCycleException(string message, System.Exception innerException)
        : base(message, innerException)
    {
    }
}
