namespace Taskdeck.Domain.Exceptions;

public class DomainException : Exception
{
    public string ErrorCode { get; }

    public DomainException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    public DomainException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

public static class ErrorCodes
{
    public const string NotFound = "NotFound";
    public const string ValidationError = "ValidationError";
    public const string WipLimitExceeded = "WipLimitExceeded";
    public const string Conflict = "Conflict";
    public const string UnexpectedError = "UnexpectedError";
}
