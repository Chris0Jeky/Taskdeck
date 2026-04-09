namespace Taskdeck.Application.DTOs;

/// <summary>
/// Response for MFA setup initiation. Contains the shared secret and provisioning URI.
/// </summary>
public record MfaSetupDto(
    string SharedSecret,
    string QrCodeUri,
    string[] RecoveryCodes);

/// <summary>
/// Request to confirm MFA setup or verify an MFA challenge.
/// </summary>
public record MfaVerifyRequest(string Code);

/// <summary>
/// Response when MFA verification is required before completing an action.
/// </summary>
public record MfaChallengeDto(string ChallengeToken, string Message);

/// <summary>
/// Status of the user's MFA configuration.
/// </summary>
public record MfaStatusDto(bool IsEnabled, bool IsSetupAvailable);
