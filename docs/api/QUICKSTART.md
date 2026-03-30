# Taskdeck API Developer Quickstart

This guide gets you from zero to making authenticated API calls in under five minutes.

## Prerequisites

- .NET 8 SDK
- Git

## 1. Start the API

```bash
git clone https://github.com/Chris0Jeky/Taskdeck.git
cd Taskdeck
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
```

The API starts on `http://localhost:5000` by default. Swagger UI is available at `http://localhost:5000/swagger`.

## 2. Register a user

```bash
curl -s -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "username": "dev",
    "email": "dev@example.com",
    "password": "P@ssw0rd123"
  }'
```

Response:

```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "user": {
    "id": "a1b2c3d4-...",
    "username": "dev",
    "email": "dev@example.com",
    "defaultRole": "Editor",
    "isActive": true,
    "createdAt": "2026-03-30T12:00:00Z",
    "updatedAt": "2026-03-30T12:00:00Z"
  }
}
```

Save the `token` value for subsequent requests.

## 3. Create a board

```bash
export TOKEN="eyJhbGciOiJIUzI1NiIs..."

curl -s -X POST http://localhost:5000/api/boards \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "name": "My First Board",
    "description": "Getting started with Taskdeck"
  }'
```

Response:

```json
{
  "id": "f5e6d7c8-...",
  "name": "My First Board",
  "description": "Getting started with Taskdeck",
  "isArchived": false,
  "createdAt": "2026-03-30T12:01:00Z",
  "updatedAt": "2026-03-30T12:01:00Z"
}
```

## 4. Add columns

```bash
export BOARD_ID="f5e6d7c8-..."

curl -s -X POST "http://localhost:5000/api/boards/$BOARD_ID/columns" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"boardId": "'$BOARD_ID'", "name": "To Do", "position": 0}'
```

## 5. Create a card

```bash
export COLUMN_ID="..."  # from the column creation response

curl -s -X POST "http://localhost:5000/api/boards/$BOARD_ID/cards" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "boardId": "'$BOARD_ID'",
    "columnId": "'$COLUMN_ID'",
    "title": "My first task",
    "description": "Created via the API"
  }'
```

## 6. Quick-capture an item

The capture pipeline lets you throw in raw text that gets triaged into a proposal:

```bash
curl -s -X POST http://localhost:5000/api/capture/items \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "boardId": "'$BOARD_ID'",
    "text": "Add dark mode support to the settings page"
  }'
```

Then enqueue it for triage (generates an automation proposal):

```bash
export CAPTURE_ID="..."  # from the capture creation response

curl -s -X POST "http://localhost:5000/api/capture/items/$CAPTURE_ID/triage" \
  -H "Authorization: Bearer $TOKEN"
```

## Interactive API docs

Browse all endpoints interactively at **http://localhost:5000/swagger**. The Swagger UI supports "Try it out" with the JWT Bearer token for authenticated requests.

## Next steps

- [Authentication guide](AUTHENTICATION.md) -- JWT flow, token refresh, GitHub OAuth
- [Boards, Columns, Cards, Labels](BOARDS.md) -- full CRUD reference with examples
- [Capture pipeline](CAPTURE.md) -- capture, triage, and proposal flow
- [Chat API](CHAT.md) -- LLM-powered chat sessions and streaming
- [Webhooks](WEBHOOKS.md) -- outbound webhook setup and signature verification
- [Error contracts](ERROR_CONTRACTS.md) -- error codes and HTTP status mapping
