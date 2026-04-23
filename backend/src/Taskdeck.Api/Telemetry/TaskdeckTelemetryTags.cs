namespace Taskdeck.Api.Telemetry;

public static class TaskdeckTelemetryTags
{
    public const string CorrelationId = "taskdeck.correlation_id";
    public const string RequestId = "taskdeck.request_id";
    public const string WorkerName = "taskdeck.worker.name";
    public const string Outcome = "taskdeck.outcome";
    public const string QueueName = "taskdeck.queue.name";
    public const string QueueDepth = "taskdeck.queue.depth";
    public const string RequestType = "taskdeck.llm.request_type";
    public const string LlmRequestId = "taskdeck.llm.request_id";
    public const string UserId = "taskdeck.user.id";
    public const string BoardId = "taskdeck.board.id";

    // ── MCP-specific tags ───────────────────────────────────────────────────
    public const string McpOperationType = "taskdeck.mcp.operation_type";
    public const string McpOperationName = "taskdeck.mcp.operation_name";
    public const string McpTransport = "taskdeck.mcp.transport";
    public const string McpErrorType = "taskdeck.mcp.error_type";
    public const string McpSuccess = "taskdeck.mcp.success";
}
