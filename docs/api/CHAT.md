# Chat API

The chat API provides LLM-powered conversational sessions that can generate automation proposals for board mutations. Chat sessions can optionally be scoped to a specific board.

## Create a chat session

```bash
curl -s -X POST http://localhost:5000/api/llm/chat/sessions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "title": "Sprint planning",
    "boardId": "f5e6d7c8-..."
  }'
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `title` | string | yes | Session display name |
| `boardId` | GUID | no | Scope the session to a board for context-aware proposals |

Response (`201 Created`):

```json
{
  "id": "aabbccdd-1122-3344-5566-778899aabbcc",
  "userId": "3fa85f64-...",
  "boardId": "f5e6d7c8-...",
  "title": "Sprint planning",
  "status": "Active",
  "createdAt": "2026-03-30T12:00:00Z",
  "updatedAt": "2026-03-30T12:00:00Z",
  "recentMessages": []
}
```

## List sessions

```bash
curl -s http://localhost:5000/api/llm/chat/sessions \
  -H "Authorization: Bearer $TOKEN"
```

Returns all sessions for the current user with recent messages.

## Get a session

```bash
curl -s "http://localhost:5000/api/llm/chat/sessions/$SESSION_ID" \
  -H "Authorization: Bearer $TOKEN"
```

## Send a message

```bash
curl -s -X POST "http://localhost:5000/api/llm/chat/sessions/$SESSION_ID/messages" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "content": "Create a card for adding dark mode support",
    "requestProposal": true
  }'
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `content` | string | yes | User message text |
| `requestProposal` | bool | no | When `true`, the LLM attempts to generate a board mutation proposal |

Response (`200 OK`):

```json
{
  "id": "eeff0011-...",
  "sessionId": "aabbccdd-...",
  "role": "Assistant",
  "content": "I've created a proposal to add a 'Dark mode support' card...",
  "messageType": "proposal",
  "proposalId": "55667788-...",
  "tokenUsage": 142,
  "createdAt": "2026-03-30T12:01:00Z",
  "degradedReason": null
}
```

### Message types

| Type | Description |
|------|-------------|
| `text` | Regular conversational response |
| `proposal` | Response includes an automation proposal (check `proposalId`) |
| `degraded` | LLM provider returned a degraded response (check `degradedReason`) |

### Degraded responses

When the LLM provider is unavailable or encounters an error, the message includes a `degradedReason` explaining what happened:

```json
{
  "role": "Assistant",
  "content": "I'm currently unable to process this request.",
  "messageType": "degraded",
  "degradedReason": "LLM provider timeout after 30s"
}
```

## Streaming responses (SSE)

For real-time token streaming, use the SSE endpoint:

```bash
curl -s -N "http://localhost:5000/api/llm/chat/sessions/$SESSION_ID/stream" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: text/event-stream"
```

Events:

```
event: message.delta
data: {"token": "I", "isComplete": false}

event: message.delta
data: {"token": "'ve created", "isComplete": false}

event: message.complete
data: {"token": "", "isComplete": true}
```

## Provider health

Check the current LLM provider status:

```bash
curl -s "http://localhost:5000/api/llm/chat/health" \
  -H "Authorization: Bearer $TOKEN"
```

Response:

```json
{
  "isAvailable": true,
  "providerName": "Mock",
  "errorMessage": null,
  "model": "mock-v1",
  "isMock": true,
  "isProbed": false
}
```

Add `?probe=true` to send a lightweight test request to the provider:

```bash
curl -s "http://localhost:5000/api/llm/chat/health?probe=true" \
  -H "Authorization: Bearer $TOKEN"
```

## Provider configuration

The default provider is `Mock` (deterministic responses for testing). Production providers (`OpenAI`, `Gemini`) are enabled via configuration. See `docs/platform/LLM_PROVIDER_SETUP_GUIDE.md` for setup details.
