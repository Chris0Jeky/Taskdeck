namespace Taskdeck.Application.Services;

public enum RetiredLlmProviderConfigurationReason
{
    ProviderSelector = 0,
    SettingsSection = 1,
    ComposeMarker = 2
}

/// <summary>
/// Identifies a known retired-provider configuration boundary without requiring callers to
/// inspect or expose configuration values or exception text.
/// </summary>
public sealed class RetiredLlmProviderConfigurationException : InvalidOperationException
{
    internal RetiredLlmProviderConfigurationException(
        RetiredLlmProviderConfigurationReason reason,
        string fixedGuidance)
        : base(fixedGuidance)
    {
        Reason = reason;
    }

    public RetiredLlmProviderConfigurationReason Reason { get; }
}
