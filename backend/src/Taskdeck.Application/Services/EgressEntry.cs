namespace Taskdeck.Application.Services;

/// <summary>
/// Describes a single known outbound data path from Taskdeck.
/// Each entry documents which host receives data, what kind of data,
/// which component initiates the connection, and the data classification.
/// </summary>
/// <param name="Host">The outbound hostname (e.g. "api.openai.com").</param>
/// <param name="PayloadCategory">Human-readable description of the payload (e.g. "LLM prompt + board context").</param>
/// <param name="ToolOrAgentName">The Taskdeck component that initiates this connection.</param>
/// <param name="Classification">Data sensitivity classification.</param>
public sealed record EgressEntry(
    string Host,
    string PayloadCategory,
    string ToolOrAgentName,
    EgressDataClassification Classification);
