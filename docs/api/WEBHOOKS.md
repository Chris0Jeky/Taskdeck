# Outbound Webhooks

Taskdeck delivers signed event payloads to external endpoints when board mutations occur. Webhooks are board-scoped and support event type filtering.

## Overview

- Webhooks are created per-board by users with board management permission.
- Each subscription receives a unique signing secret (shown once at creation).
- Payloads are signed with HMAC-SHA256 for authenticity verification.
- Failed deliveries are retried with exponential backoff; permanently failed deliveries move to dead-letter state.

## Create a webhook subscription

```bash
curl -s -X POST "http://localhost:5000/api/boards/$BOARD_ID/webhooks" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "endpointUrl": "https://example.com/hooks/taskdeck",
    "eventFilters": ["card.created", "card.moved", "card.deleted"]
  }'
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `endpointUrl` | string | yes | HTTPS URL that receives webhook payloads |
| `eventFilters` | string[] | no | Event types to subscribe to (null = all events) |

Response (`201 Created`):

```json
{
  "subscription": {
    "id": "11223344-...",
    "boardId": "f5e6d7c8-...",
    "endpointUrl": "https://example.com/hooks/taskdeck",
    "eventFilters": ["card.created", "card.moved", "card.deleted"],
    "isActive": true,
    "createdAt": "2026-03-30T12:00:00Z",
    "updatedAt": "2026-03-30T12:00:00Z",
    "revokedAt": null,
    "lastTriggeredAt": null
  },
  "signingSecret": "whsec_a1b2c3d4e5f6..."
}
```

**Important:** The `signingSecret` is only returned at creation time and after secret rotation. Store it securely.

## List subscriptions

```bash
curl -s "http://localhost:5000/api/boards/$BOARD_ID/webhooks" \
  -H "Authorization: Bearer $TOKEN"
```

Returns an array of `OutboundWebhookSubscriptionDto` (without signing secrets).

## Rotate signing secret

If you suspect a secret has been compromised, rotate it:

```bash
curl -s -X POST "http://localhost:5000/api/boards/$BOARD_ID/webhooks/$SUBSCRIPTION_ID/rotate-secret" \
  -H "Authorization: Bearer $TOKEN"
```

Returns the subscription with the new `signingSecret`. The old secret is immediately invalidated.

## Revoke a subscription

```bash
curl -s -X DELETE "http://localhost:5000/api/boards/$BOARD_ID/webhooks/$SUBSCRIPTION_ID" \
  -H "Authorization: Bearer $TOKEN"
```

Response: `204 No Content`. Pending deliveries are cancelled.

## Event types

| Event type | Description |
|------------|-------------|
| `card.created` | A card was created on the board |
| `card.updated` | A card's fields were modified |
| `card.moved` | A card was moved to a different column |
| `card.deleted` | A card was deleted |
| `column.created` | A column was added to the board |
| `column.updated` | A column was modified |
| `column.deleted` | A column was removed |
| `board.updated` | The board itself was modified |

## Webhook payload format

Payloads are delivered as JSON via HTTP POST:

```json
{
  "id": "delivery-uuid",
  "subscriptionId": "11223344-...",
  "eventType": "card.created",
  "boardId": "f5e6d7c8-...",
  "timestamp": "2026-03-30T12:05:00Z",
  "payload": {
    "cardId": "d4e5f6a7-...",
    "columnId": "a1b2c3d4-...",
    "title": "New card title",
    "createdBy": "3fa85f64-..."
  }
}
```

## Delivery headers

Every webhook delivery includes these custom headers:

| Header | Description |
|--------|-------------|
| `X-Taskdeck-Webhook-Delivery-Id` | Unique delivery identifier (GUID) |
| `X-Taskdeck-Webhook-Subscription-Id` | The subscription that triggered this delivery |
| `X-Taskdeck-Webhook-Event` | The event type (e.g., `card.created`) |
| `X-Taskdeck-Webhook-Timestamp` | Unix epoch seconds when the delivery was signed |
| `X-Taskdeck-Webhook-Signature` | `sha256={hex-encoded HMAC}` for payload verification |

## Signature verification

The `X-Taskdeck-Webhook-Signature` header contains a `sha256=` prefixed HMAC-SHA256 signature. The canonical signing string is `{timestamp}.{payload}` where `{timestamp}` is the Unix epoch seconds from the `X-Taskdeck-Webhook-Timestamp` header and `{payload}` is the raw request body.

### Verification algorithm

1. Extract the `X-Taskdeck-Webhook-Timestamp` header value.
2. Extract the hex signature from `X-Taskdeck-Webhook-Signature` (strip the `sha256=` prefix).
3. Build the canonical string: `{timestamp}.{rawBody}`.
4. Compute HMAC-SHA256 of the canonical string using your signing secret as the key.
5. Hex-encode the result (lowercase).
6. Compare with the extracted signature using constant-time comparison.

### Example: Node.js verification

```javascript
const crypto = require('crypto');

function verifySignature(rawBody, timestampHeader, signatureHeader, secret) {
  // Strip the "sha256=" prefix
  const signature = signatureHeader.replace('sha256=', '');
  // Build the canonical signing string
  const canonical = `${timestampHeader}.${rawBody}`;
  const expected = crypto
    .createHmac('sha256', secret)
    .update(canonical, 'utf8')
    .digest('hex');

  return crypto.timingSafeEqual(
    Buffer.from(signature, 'hex'),
    Buffer.from(expected, 'hex')
  );
}

// In your webhook handler:
app.post('/hooks/taskdeck', (req, res) => {
  const signature = req.headers['x-taskdeck-webhook-signature'];
  const timestamp = req.headers['x-taskdeck-webhook-timestamp'];
  const rawBody = req.rawBody; // ensure you capture the raw body

  if (!verifySignature(rawBody, timestamp, signature, process.env.TASKDECK_WEBHOOK_SECRET)) {
    return res.status(401).send('Invalid signature');
  }

  const event = JSON.parse(rawBody);
  console.log(`Received ${req.headers['x-taskdeck-webhook-event']} event`);
  res.status(200).send('OK');
});
```

### Example: Python verification

```python
import hmac
import hashlib

def verify_signature(raw_body: str, timestamp: str, signature_header: str, secret: str) -> bool:
    # Strip the "sha256=" prefix
    signature = signature_header.removeprefix("sha256=")
    # Build the canonical signing string
    canonical = f"{timestamp}.{raw_body}"
    expected = hmac.new(
        secret.encode('utf-8'),
        canonical.encode('utf-8'),
        hashlib.sha256
    ).hexdigest()
    return hmac.compare_digest(expected, signature)
```

### Example: C# verification

```csharp
using System.Security.Cryptography;
using System.Text;

bool VerifySignature(string rawBody, string timestamp, string signatureHeader, string secret)
{
    // Strip the "sha256=" prefix
    var signature = signatureHeader.Replace("sha256=", "");
    // Build the canonical signing string
    var canonical = $"{timestamp}.{rawBody}";
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
    var expected = Convert.ToHexString(hash).ToLowerInvariant();
    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(expected),
        Encoding.UTF8.GetBytes(signature));
}
```

## Delivery behavior

- Successful delivery: endpoint returns `2xx` status.
- Retry on failure: deliveries are retried with exponential backoff.
- Dead-letter: after exhausting retries, the delivery is moved to dead-letter state for manual inspection.
- Localhost endpoints: HTTP is only allowed for localhost endpoints when explicitly configured; all other endpoints must use HTTPS.

## Security considerations

- Always verify the `X-Taskdeck-Webhook-Signature` header before processing payloads.
- Check the `X-Taskdeck-Webhook-Timestamp` to reject stale deliveries (replay protection).
- Use HTTPS endpoints only (HTTP is only allowed for localhost in development).
- Rotate secrets periodically and immediately if compromise is suspected.
- Respond with `200 OK` quickly; process the event asynchronously if needed.
