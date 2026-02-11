# LLM Queue System Guide

**Status:** Scaffolding Complete - Implementation Pending  
**Last Updated:** 2026-02-11

## Overview

The LLM Queue System enables queuing of AI/LLM requests (voicenotes, transcripts, text commands) for processing when the LLM service is available. This solves the problem of local LLM downtime and enables asynchronous AI-powered task management.

## Use Cases

1. **Offline LLM:** Queue requests when local LLM is down, process when it comes online
2. **Voice Notes:** Record voice notes, queue transcription, process when ready
3. **Batch Processing:** Queue multiple requests, process in background
4. **Rate Limiting:** Manage LLM request volume with controlled processing
5. **Retry Logic:** Automatically retry failed requests
6. **Audit Trail:** Track all LLM interactions

## Architecture

### Domain Entity: LlmRequest

**Properties:**
- `Id`: Unique request identifier
- `UserId`: User who submitted the request
- `BoardId`: Optional board context
- `RequestType`: Type of request (voicenote, transcript, command, etc.)
- `Payload`: Request data (JSON-encoded)
- `Status`: Current status (Pending, Processing, Completed, Failed, Cancelled)
- `ErrorMessage`: Error details if failed
- `ProcessedAt`: When request was processed
- `RetryCount`: Number of retry attempts

**Operations:**
- `MarkAsProcessing()`: Start processing
- `MarkAsCompleted()`: Mark as done
- `MarkAsFailed(error)`: Mark as failed with error
- `Cancel()`: Cancel pending request
- `ResetForRetry()`: Reset failed request for retry

### Request Types

**Supported Types:**
- `voicenote`: Audio file to be transcribed and processed
- `transcript`: Text transcript to be analyzed
- `command`: Natural language command to be executed
- `batch`: Multiple requests bundled together
- `analysis`: Request for board/task analysis

### Status Flow

```
Pending → Processing → Completed
              ↓
           Failed → (Retry) → Pending
              ↓
          Cancelled
```

## Service Interface

**Service:** `ILlmQueueService` (Implementation Pending)

**Operations:**
```csharp
Task<Result<LlmRequestDto>> AddToQueueAsync(CreateLlmRequestDto dto);
Task<Result<IEnumerable<LlmRequestDto>>> GetUserQueueAsync(Guid userId);
Task<Result<IEnumerable<LlmRequestDto>>> GetQueueByStatusAsync(RequestStatus status);
Task<Result> CancelRequestAsync(Guid requestId, Guid userId);
Task<Result<LlmRequestDto>> ProcessNextRequestAsync();
Task<Result<QueueStatsDto>> GetQueueStatsAsync();
```

## Request Processing Flow

### 1. Submission

```
User submits request
  → Validate input
  → Create LlmRequest entity (Status: Pending)
  → Save to database
  → Return request ID to user
```

### 2. Processing

```
Background processor picks up request
  → Mark as Processing
  → Call LLM service
  → Parse response
  → Update entities (create cards, update boards, etc.)
  → Mark as Completed
  OR
  → Mark as Failed (with error message)
```

### 3. Retry Logic

```
If Failed and RetryCount < MaxRetries:
  → Wait exponential backoff time
  → ResetForRetry()
  → Add back to queue
Else:
  → Notify user of permanent failure
```

## Payload Format

### Voice Note Request

```json
{
  "type": "voicenote",
  "data": {
    "audioFile": "base64-encoded-audio",
    "format": "mp3",
    "duration": 45,
    "boardId": "guid-here"
  }
}
```

### Transcript Request

```json
{
  "type": "transcript",
  "data": {
    "text": "Create a high priority card for implementing user authentication",
    "boardId": "guid-here",
    "context": "board"
  }
}
```

### Command Request

```json
{
  "type": "command",
  "data": {
    "command": "analyze board progress",
    "boardId": "guid-here"
  }
}
```

## Background Processor

### Implementation Strategy

**Option 1: Hosted Service (.NET)**
```csharp
public class LlmQueueProcessor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessNextRequestAsync();
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
```

**Option 2: Separate Worker Process**
- Standalone console app
- Polls queue periodically
- Can be scaled independently

**Option 3: Triggered by API**
- Manual trigger via endpoint
- Scheduled via cron job
- On-demand processing only

### Configuration

```json
{
  "LlmQueue": {
    "ProcessingIntervalSeconds": 10,
    "MaxConcurrentRequests": 3,
    "MaxRetries": 3,
    "RetryDelaySeconds": 60,
    "EnableAutoProcessing": true,
    "LlmServiceUrl": "http://localhost:11434",
    "LlmModel": "llama2"
  }
}
```

## CLI Commands (To Be Implemented)

### Queue Management

```bash
# Add request to queue
taskdeck queue add --type voicenote --file audio.mp3 --board <board-id>
taskdeck queue add --type transcript --text "Create card for X" --board <board-id>

# List queue
taskdeck queue list
taskdeck queue list --status pending
taskdeck queue list --user <user-id>

# View request details
taskdeck queue get --id <request-id>

# Cancel request
taskdeck queue cancel --id <request-id>

# Process queue manually
taskdeck queue process
taskdeck queue process --limit 10

# View stats
taskdeck queue stats
```

## API Endpoints (To Be Implemented)

### Queue Operations

```
POST   /api/llm/queue              - Add request to queue
GET    /api/llm/queue              - List user's queue
GET    /api/llm/queue/{id}         - Get request details
DELETE /api/llm/queue/{id}         - Cancel request
POST   /api/llm/queue/process      - Trigger processing
GET    /api/llm/queue/stats        - Get queue statistics
```

## Integration with LLM Service

### LLM Service Interface (To Be Implemented)

```csharp
public interface ILlmService
{
    Task<LlmResponse> ProcessVoiceNoteAsync(string audioData, string format);
    Task<LlmResponse> ProcessTranscriptAsync(string text, string context);
    Task<LlmResponse> ProcessCommandAsync(string command, Guid? boardId);
    Task<bool> IsAvailableAsync();
}
```

### LLM Response Format

```csharp
public record LlmResponse(
    bool Success,
    string ResponseText,
    IEnumerable<LlmAction> Actions,
    string? ErrorMessage
);

public record LlmAction(
    string ActionType,  // "create_card", "update_board", etc.
    Dictionary<string, object> Parameters
);
```

## Implementation Checklist

### Phase 1: Queue Infrastructure
- [ ] Implement `LlmQueueService`
- [ ] Create queue API endpoints
- [ ] Add CLI queue commands
- [ ] Write queue management tests

### Phase 2: Background Processor
- [ ] Create `LlmQueueProcessor` background service
- [ ] Implement polling logic
- [ ] Add retry mechanism
- [ ] Configure processing intervals
- [ ] Write processor tests

### Phase 3: LLM Integration
- [ ] Create `ILlmService` interface
- [ ] Implement Ollama integration (or other LLM)
- [ ] Add voice transcription (Whisper)
- [ ] Parse LLM responses into actions
- [ ] Write integration tests

### Phase 4: Action Execution
- [ ] Implement action executor
- [ ] Handle "create_card" action
- [ ] Handle "update_board" action
- [ ] Handle "move_card" action
- [ ] Add transaction support
- [ ] Write action execution tests

### Phase 5: Advanced Features
- [ ] Add batch request support
- [ ] Implement priority queue
- [ ] Add webhook notifications
- [ ] Create monitoring dashboard
- [ ] Add metrics and logging

## Security Considerations

1. **Authorization:** Users can only access their own queue items
2. **Payload Validation:** Sanitize and validate all inputs
3. **Rate Limiting:** Limit requests per user per time period
4. **Resource Limits:** Cap audio file sizes, text lengths
5. **Audit Trail:** Log all queue operations

## Error Handling

### Retry Strategy

**Exponential Backoff:**
- Attempt 1: Immediate
- Attempt 2: Wait 60 seconds
- Attempt 3: Wait 120 seconds
- Attempt 4+: Wait 300 seconds

**Max Retries:** 3 (configurable)

### Error Types

- `LlmServiceUnavailable`: LLM is down, retry
- `InvalidPayload`: Bad request data, don't retry
- `AuthenticationFailed`: LLM auth issue, don't retry
- `RateLimitExceeded`: Too many requests, retry with delay
- `UnknownError`: Unexpected error, retry once

## Monitoring & Observability

### Metrics to Track

- Queue size by status
- Average processing time
- Success/failure rates
- Retry counts
- LLM service availability

### Logging

- Request submission
- Processing start/end
- Failures with full error details
- Retry attempts

## Usage Examples (Future)

### Adding to Queue

```csharp
var result = await llmQueueService.AddToQueueAsync(new CreateLlmRequestDto(
    UserId: currentUserId,
    RequestType: "transcript",
    Payload: JsonSerializer.Serialize(new {
        text = "Create urgent card for bug fix",
        boardId = boardId
    }),
    BoardId: boardId
));
```

### Processing Queue

```csharp
// In background service
var request = await llmQueueService.ProcessNextRequestAsync();
if (request.IsSuccess)
{
    // Request was processed successfully
    logger.LogInformation($"Processed request {request.Value.Id}");
}
```

## Next Steps

1. Review queue architecture design
2. Implement `LlmQueueService`
3. Create background processor
4. Integrate with LLM service (Ollama)
5. Add queue API endpoints
6. Add CLI commands
7. Write comprehensive tests

## References

- Domain Entity: `backend/src/Taskdeck.Domain/Entities/LlmRequest.cs`
- Service Interface: `backend/src/Taskdeck.Application/Services/ILlmQueueService.cs`
- Repository: `backend/src/Taskdeck.Infrastructure/Repositories/LlmQueueRepository.cs`
- DTOs: `backend/src/Taskdeck.Application/DTOs/LlmQueueDtos.cs`
