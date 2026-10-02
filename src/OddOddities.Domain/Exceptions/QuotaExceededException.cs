namespace OddOddities.Domain.Exceptions;

/// <summary>
/// Thrown when the MinIO bucket quota (BR-009) is exceeded.
/// Indicates that the storage limit has been reached and uploads are blocked.
/// </summary>
public sealed class QuotaExceededException : Exception
{
    /// <summary>The configured quota limit in bytes.</summary>
    public long QuotaBytes { get; }

    /// <summary>The current usage in bytes.</summary>
    public long CurrentUsageBytes { get; }

    /// <summary>The size of the attempted upload in bytes.</summary>
    public long AttemptedUploadBytes { get; }

    public QuotaExceededException(
        long quotaBytes,
        long currentUsageBytes,
        long attemptedUploadBytes)
        : base(
            $"MinIO quota of {quotaBytes:N0} bytes exceeded. " +
            $"Current usage: {currentUsageBytes:N0} bytes. " +
            $"Attempted upload: {attemptedUploadBytes:N0} bytes.")
    {
        QuotaBytes = quotaBytes;
        CurrentUsageBytes = currentUsageBytes;
        AttemptedUploadBytes = attemptedUploadBytes;
    }

    public QuotaExceededException(
        long quotaBytes,
        long currentUsageBytes,
        long attemptedUploadBytes,
        Exception innerException)
        : base(
            $"MinIO quota of {quotaBytes:N0} bytes exceeded. " +
            $"Current usage: {currentUsageBytes:N0} bytes. " +
            $"Attempted upload: {attemptedUploadBytes:N0} bytes.",
            innerException)
    {
        QuotaBytes = quotaBytes;
        CurrentUsageBytes = currentUsageBytes;
        AttemptedUploadBytes = attemptedUploadBytes;
    }
}
