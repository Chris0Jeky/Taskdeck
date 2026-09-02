namespace Taskdeck.Acceleration.Candidates.Operations;

/// <summary>
/// Stable process-exit contract for automation and runbooks.
/// Keep human prose on stderr/stdout, but branch on these codes only.
/// </summary>
public static class OpsCommandExitCodes
{
    public const int Success = 0;
    public const int UsageError = 2;
    public const int PreconditionsNotMet = 10;
    public const int VerificationFailed = 20;
    public const int BackupFailed = 30;
    public const int RestoreFailed = 31;
    public const int DataIntegrityFailed = 40;
    public const int UnexpectedFailure = 70;
}
