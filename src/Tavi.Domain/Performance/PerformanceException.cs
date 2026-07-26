namespace Tavi.Domain.Performance;

public static class PerformanceErrorCodes
{
    public const string InvalidArgument = "TAVI.PERFORMANCE.ARGUMENT.INVALID";
    public const string NotFound = "TAVI.PERFORMANCE.ENTITY.NOT_FOUND";
    public const string Duplicate = "TAVI.PERFORMANCE.ENTITY.DUPLICATE";
    public const string InvalidSnapshot = "TAVI.PERFORMANCE.SNAPSHOT.INVALID";
    public const string RollbackFailed = "TAVI.PERFORMANCE.TRANSACTION.ROLLBACK_FAILED";
}

public sealed class PerformanceException : TaviException
{
    public PerformanceException(string errorCode, TaviErrorCategory category, string operation, string message, Exception? innerException = null)
        : base(errorCode, category, message, operation, innerException: innerException)
    {
    }
}
