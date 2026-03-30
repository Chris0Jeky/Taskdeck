# Authentication

Taskdeck uses JWT Bearer tokens for API authentication. All endpoints except `/api/auth/*` and `/api/health` require a valid token.

## Obtaining a token

### Register a new account

```bash
curl -s -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "username": "alice",
    "email": "alice@example.com",
    "password": "SecureP@ss1"
  }'
```

Response (`200 OK`):

```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "user": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "username": "alice",
    "email": "alice@example.com",
    "defaultRole": "Editor",
    "isActive": true,
    "createdAt": "2026-03-30T10:00:00Z",
    "updatedAt": "2026-03-30T10:00:00Z"
  }
}
```

### Login with existing credentials

```bash
curl -s -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "usernameOrEmail": "alice",
    "password": "SecureP@ss1"
  }'
```

The response shape is identical to registration.

### Failed login

```json
{
  "errorCode": "AuthenticationFailed",
  "message": "Invalid username/email or password"
}
```

HTTP status: `401 Unauthorized`

## Using the token

Include the token in the `Authorization` header with the `Bearer` scheme:

```bash
curl -s http://localhost:5000/api/boards \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIs..."
```

### Missing or invalid token

If the token is missing, expired, or invalid, the API returns:

```json
{
  "errorCode": "AuthenticationFailed",
  "message": "Authentication is required to access this resource"
}
```

HTTP status: `401 Unauthorized`

### Insufficient permissions

If the token is valid but the user lacks permission for the requested resource:

```json
{
  "errorCode": "Forbidden",
  "message": "You do not have permission to access this resource"
}
```

HTTP status: `403 Forbidden`

## JWT claims

The JWT payload contains these claims:

| Claim | Description |
|-------|-------------|
| `sub` | User ID (GUID) |
| `unique_name` | Username |
| `email` | Email address |
| `role` | Default role (e.g., `Editor`, `Admin`) |
| `exp` | Expiration timestamp (Unix epoch) |
| `iss` | Issuer (`Taskdeck`) |
| `aud` | Audience (`Taskdeck`) |

## Change password

```bash
curl -s -X POST http://localhost:5000/api/auth/change-password \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "currentPassword": "SecureP@ss1",
    "newPassword": "NewSecureP@ss2"
  }'
```

Response: `204 No Content` on success.

## GitHub OAuth (optional)

When the Taskdeck instance has GitHub OAuth configured, users can authenticate via GitHub.

### Check provider availability

```bash
curl -s http://localhost:5000/api/auth/providers
```

```json
{
  "gitHub": true
}
```

### OAuth flow

1. **Redirect the user** to `GET /api/auth/github/login?returnUrl=/` -- this initiates the GitHub OAuth handshake.
2. **GitHub callback** -- after authorization, the user is redirected back with a short-lived `oauth_code` query parameter.
3. **Exchange the code** for a JWT:

```bash
curl -s -X POST http://localhost:5000/api/auth/github/exchange \
  -H "Content-Type: application/json" \
  -d '{"code": "the-oauth-code-from-redirect"}'
```

Response is the same `AuthResultDto` shape (token + user).

The authorization code is single-use and expires after 60 seconds.

## Rate limiting

Auth endpoints are rate-limited per IP address. When the limit is exceeded:

```json
{
  "errorCode": "TooManyRequests",
  "message": "Rate limit exceeded"
}
```

HTTP status: `429 Too Many Requests`

## Request correlation

Every API response includes an `X-Request-Id` header. Include this ID when reporting issues for server-side log correlation.
