using System.Text.Json;

namespace Taskdeck.Application.Services.Tools;

/// <summary>
/// Shared JSON serializer options for tool result serialization.
/// Uses snake_case to match the tool schema naming convention.
/// </summary>
internal static class ToolJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };
}
