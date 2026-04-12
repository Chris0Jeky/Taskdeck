namespace Taskdeck.Application.Services;

/// <summary>
/// Configuration for MFA policy. Controls which actions require MFA verification.
/// MFA is always optional unless explicitly enabled by the administrator.
/// </summary>
public class MfaPolicySettings
{
    /// <summary>
    /// Whether MFA setup is available to users. Does not force MFA on anyone.
    /// </summary>
    public bool EnableMfaSetup { get; set; }

    /// <summary>
    /// When true, users who have MFA enabled will be required to verify
    /// before performing sensitive actions (password change, account deletion).
    /// </summary>
    public bool RequireMfaForSensitiveActions { get; set; }

    /// <summary>
    /// TOTP time step in seconds. Standard is 30.
    /// </summary>
    public int TotpTimeStepSeconds { get; set; } = 30;

    /// <summary>
    /// Number of recovery codes to generate during MFA setup.
    /// </summary>
    public int RecoveryCodeCount { get; set; } = 8;

    /// <summary>
    /// Number of adjacent time windows to accept for TOTP validation.
    /// A value of 1 means current + 1 before + 1 after = 3 windows.
    /// </summary>
    public int TotpToleranceSteps { get; set; } = 1;
}
