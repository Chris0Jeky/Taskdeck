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

## Signature verification

Every delivery includes an `X-Taskdeck-Signature` header containing an HMAC-SHA256 signature of the request body.

### Verification algorithm

1. Read the raw request body as UTF-8 bytes.
2. Compute HMAC-SHA256 using your signing secret as the key.
3. Hex-encode the result.
4. Compare with the `X-Taskdeck-Signature` header value using constant-time comparison.

### Example: Node.js verification

```javascript
const crypto = require('crypto');

function verifySignature(rawBody, signature, secret) {
  const expected = crypto
    .createHmac('sha256', secret)
    .update(rawBody, 'utf8')
    .digest('hex');

  return crypto.timingSafeEqual(
    Buffer.from(signature, 'hex'),
    Buffer.from(expected, 'hex')
  );
}

// In your webhook handler:
app.post('/hooks/taskdeck', (req, res) => {
  const signature = req.headers['x-taskdeck-signature'];
  const rawBody = req.rawBody; // ensure you capture the raw body

  if (!verifySignature(rawBody, signature, process.env.TASKDECK_WEBHOOK_SECRET)) {
    return res.status(401).send('Invalid signature');
  }

  const event = JSON.parse(rawBody);
  console.log(`Received ${event.eventType} for board ${event.boardId}`);
  res.status(200).send('OK');
});
```

### Example: Python verification

```python
import hmac
import hashlib

def verify_signature(raw_body: bytes, signature: str, secret: str) -> bool:
    expected = hmac.new(
        secret.encode('utf-8'),
        raw_body,
        hashlib.sha256
    ).hexdigest()
    return hmac.compare_digest(expected, signature)
```

### Example: C# verification

```csharp
using System.Security.Cryptography;
using System.Text;

bool VerifySignature(string rawBody, string signature, string secret)
{
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
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
- Timeout: the webhook delivery worker waits up to 30 seconds for a response.

## Security considerations

- Always verify the `X-Taskdeck-Signature` header before processing payloads.
- Use HTTPS endpoints only.
- Rotate secrets periodically and immediately if compromise is suspected.
- Respond with `200 OK` quickly; process the event asynchronously if needed.
