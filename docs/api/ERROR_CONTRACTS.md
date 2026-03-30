# Error Contracts

All Taskdeck API error responses follow a consistent shape. Understanding these contracts helps you build resilient integrations.

## Error response format

Every error response body has this structure:

```json
{
  "errorCode": "NotFound",
  "message": "Board not found or not accessible"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `errorCode` | string | Machine-readable error identifier (see table below) |
| `message` | string | Human-readable description of the error |

## Error codes

| Error code | HTTP status | Description |
|------------|-------------|-------------|
| `NotFound` | 404 | The requested resource does not exist or is not accessible to the current user |
| `ValidationError` | 400 | Request body or query parameters failed validation |
| `WipLimitExceeded` | 400 | Card operation would exceed the column's WIP (work-in-progress) limit |
| `Conflict` | 409 | Resource was modified concurrently (e.g., stale `ExpectedUpdatedAt`) |
| `UnexpectedError` | 500 | An unexpected server-side error occurred |
| `Unauthorized` | 401 | The request lacks valid authentication credentials |
| `Forbidden` | 403 | The authenticated user does not have permission for this operation |
| `TooManyRequests` | 429 | Rate limit exceeded; retry after the indicated period |
| `AuthenticationFailed` | 401 | Login credentials are invalid or the token has expired |
| `InvalidOperation` | 400 | The operation is not valid in the current resource state |
| `LlmQuotaExceeded` | 429 | The user's LLM usage quota has been exceeded |
| `LlmKillSwitchActive` | 503 | The LLM provider has been disabled by an operator |
| `AbuseContainmentActive` | 403 | The user account is under abuse containment restrictions |

## HTTP status code mapping

| Status | Meaning |
|--------|---------|
| `200 OK` | Request succeeded, response body contains the result |
| `201 Created` | Resource created, `Location` header points to the new resource |
| `202 Accepted` | Request accepted for asynchronous processing |
| `204 No Content` | Request succeeded, no response body (e.g., delete operations) |
| `400 Bad Request` | Client error in the request body or parameters |
| `401 Unauthorized` | Authentication required or credentials invalid |
| `403 Forbidden` | Authenticated but insufficient permissions |
| `404 Not Found` | Resource not found or not accessible |
| `409 Conflict` | Concurrent modification conflict |
| `429 Too Many Requests` | Rate limit exceeded |
| `500 Internal Server Error` | Unexpected server error |
| `503 Service Unavailable` | Dependent service is unavailable (e.g., LLM kill switch) |

## Request correlation

Every response includes an `X-Request-Id` header. This ID is logged server-side and can be used for debugging and support requests:

```
X-Request-Id: 7f3a8b2c-1d4e-5f6a-b7c8-d9e0f1234567
```

You can also supply your own correlation ID by sending the `X-Request-Id` header in the request. The server will echo it back.

## Handling errors in integrations

### Retry strategy

| Error code | Retryable | Strategy |
|------------|-----------|----------|
| `TooManyRequests` | Yes | Respect `Retry-After` header, use exponential backoff |
| `LlmQuotaExceeded` | Yes (later) | Wait for quota reset period |
| `UnexpectedError` | Maybe | Retry with backoff, report if persistent |
| `Conflict` | Yes | Re-fetch the resource, resolve conflict, retry |
| `LlmKillSwitchActive` | No (temporary) | Wait for operator to re-enable the provider |
| All others | No | Fix the request and retry |

### Example: error handling in JavaScript

```javascript
async function taskdeckFetch(url, options) {
  const response = await fetch(url, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`,
      ...options?.headers,
    },
  });

  if (!response.ok) {
    const error = await response.json();
    const requestId = response.headers.get('X-Request-Id');

    switch (error.errorCode) {
      case 'TooManyRequests':
        // Back off and retry
        const retryAfter = response.headers.get('Retry-After') || '5';
        await sleep(parseInt(retryAfter) * 1000);
        return taskdeckFetch(url, options);

      case 'Conflict':
        // Re-fetch and resolve
        throw new ConflictError(error.message, requestId);

      case 'AuthenticationFailed':
        // Token may be expired; re-authenticate
        throw new AuthError(error.message, requestId);

      default:
        throw new TaskdeckError(error.errorCode, error.message, requestId);
    }
  }

  if (response.status === 204) return null;
  return response.json();
}
```
