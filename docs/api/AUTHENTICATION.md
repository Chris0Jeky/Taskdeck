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

## OIDC/SSO Login (optional)

When OIDC providers are configured, users can authenticate via external identity providers (Microsoft Entra ID, Google, or a generic OIDC provider).

OIDC is disabled by default. When enabled via configuration, login buttons appear on the frontend LoginView for each configured provider.

### OIDC flow

1. **Redirect the user** to `GET /api/auth/oidc/{provider}/login?returnUrl=/` -- initiates the OIDC handshake with the configured provider.
2. **Provider callback** -- after authorization, the user is redirected back with a short-lived authorization code.
3. **Exchange the code** for a JWT:

```bash
curl -s -X POST http://localhost:5000/api/auth/oidc/exchange \
  -H "Content-Type: application/json" \
  -d '{"code": "the-oidc-auth-code", "provider": "microsoft"}'
```

Response is the same `AuthResultDto` shape (token + user).

### Identity isolation

Each OIDC identity is stored with a composite key (`provider + providerUserId`). There is no auto-linking by email to prevent account takeover.

## MFA (optional TOTP)

When MFA is enabled via `MfaPolicy` configuration, users can set up TOTP-based multi-factor authentication.

### Setup MFA

```bash
curl -s -X POST http://localhost:5000/api/mfa/setup \
  -H "Authorization: Bearer <token>"
```

Response (`200 OK`):

```json
{
  "totpUri": "otpauth://totp/Taskdeck:alice?secret=...",
  "recoveryCodes": ["code1", "code2", "...", "code8"]
}
```

Scan the `totpUri` with an authenticator app. Save the 8 recovery codes securely -- they are bcrypt-hashed at rest and cannot be retrieved again.

### Confirm MFA setup

```bash
curl -s -X POST http://localhost:5000/api/mfa/confirm \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"code": "123456"}'
```

Response: `204 No Content` on success.

### Verify MFA (for protected actions)

When `RequireMfaForSensitiveActions` is enabled, password changes and account deletion require MFA verification:

```bash
curl -s -X POST http://localhost:5000/api/mfa/verify \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"code": "123456"}'
```

Response: `200 OK` with a short-lived MFA verification token.

### Use a recovery code

If the authenticator app is unavailable, use a recovery code (each code is single-use):

```bash
curl -s -X POST http://localhost:5000/api/mfa/verify \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"recoveryCode": "code1"}'
```

### Disable MFA

```bash
curl -s -X POST http://localhost:5000/api/mfa/disable \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"code": "123456"}'
```

Response: `204 No Content` on success.

## API Key Management

API keys provide authentication for MCP HTTP transport and programmatic access. Keys use a `tdsk_` prefix and are SHA-256 hashed at rest.

### Create an API key

```bash
curl -s -X POST http://localhost:5000/api/api-keys \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"name": "My MCP Key"}'
```

Response (`201 Created`):

```json
{
  "id": "...",
  "name": "My MCP Key",
  "key": "tdsk_...",
  "createdAt": "2026-04-12T10:00:00Z"
}
```

The `key` value is only returned at creation time. Store it securely.

### List API keys

```bash
curl -s http://localhost:5000/api/api-keys \
  -H "Authorization: Bearer <token>"
```

Returns key metadata (id, name, createdAt, lastUsedAt) without the key value.

### Revoke an API key

```bash
curl -s -X DELETE http://localhost:5000/api/api-keys/{id} \
  -H "Authorization: Bearer <token>"
```

Response: `204 No Content` on success.

### Using API keys

API keys are used as Bearer tokens on the `/mcp` HTTP transport endpoint:

```bash
curl -s http://localhost:5000/mcp \
  -H "Authorization: Bearer tdsk_..."
```

Rate limiting: 60 requests per 60 seconds per API key.

## Account Linking

Existing users can link or unlink external identity providers from their account.

### Link GitHub account

```bash
curl -s -X POST http://localhost:5000/api/auth/github/link \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"code": "github-oauth-code"}'
```

Response: `200 OK` on success. Returns `409 Conflict` if the GitHub account is already linked to another user.

### Unlink GitHub account

```bash
curl -s -X DELETE http://localhost:5000/api/auth/github/link \
  -H "Authorization: Bearer <token>"
```

Response: `204 No Content` on success.

### List linked accounts

```bash
curl -s http://localhost:5000/api/auth/linked-accounts \
  -H "Authorization: Bearer <token>"
```

Returns a list of linked external providers with avatar and display name.

## Request correlation

Every API response includes an `X-Request-Id` header. Include this ID when reporting issues for server-side log correlation.
