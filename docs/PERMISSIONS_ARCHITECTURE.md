# Multi-User Permissions Architecture Guide

**Status:** Scaffolding Complete - Implementation Pending
**Last Updated:** 2026-02-11

## Overview

Taskdeck now includes foundational architecture for multi-user access control, database sharing, audit trails, and LLM request queuing. This guide outlines the implemented scaffolding and next steps for full implementation.

## Architecture Components

### 1. User Management

**Domain Entity:** `User`
- Properties: Username, Email, PasswordHash, DefaultRole, IsActive
- Validation: Username uniqueness, email format, minimum lengths
- Operations: Create, update profile, change password, activate/deactivate

**Repository:** `IUserRepository`
- Lookups by username, email, or ID
- Existence checks for registration validation

**Service Interface:** `IUserService` (Implementation Pending)
- User CRUD operations
- Profile management

### 2. Authentication

**Service Interface:** `IAuthenticationService` (Implementation Pending)
- Login with username/email and password
- User registration
- Password changes
- JWT token generation and validation (to be implemented)

**Architecture Decision:** JWT-based stateless authentication
- Tokens contain user ID and roles
- Refresh tokens for long-lived sessions
- Token expiration configurable

### 3. Authorization & Permissions

**Domain Entity:** `BoardAccess`
- Defines user access to specific boards
- Role-based permissions: Owner, Admin, Editor, Viewer
- Tracks who granted access and when

**Permission Levels:**
- **Owner:** Full control including deletion and ownership transfer
- **Admin:** Manage content and grant permissions (except ownership)
- **Editor:** Create, modify, delete cards/columns/labels
- **Viewer:** Read-only access

**Repository:** `IBoardAccessRepository`
- Query permissions by board or user
- Check access with minimum role requirement
- Manage board-user relationships

**Service Interface:** `IAuthorizationService` (Implementation Pending)
- Permission checking methods (CanRead, CanWrite, CanManage, CanDelete)
- Role lookup for board-user combinations

**Service Interface:** `IBoardAccessService` (Implementation Pending)
- Grant/revoke board access
- Update user roles
- List board permissions

### 4. Board Ownership

**Board Entity Updated:**
- `OwnerId` property added (nullable for backward compatibility)
- `TransferOwnership()` method for changing owners
- `BoardAccesses` navigation property for permissions

**Migration Strategy:**
- Existing boards have `null` OwnerId (system/legacy boards)
- New boards require owner assignment
- Can batch-migrate existing boards to a default user

## Database Schema

### New Tables

**Users**
```
Id (PK, GUID)
Username (unique, indexed)
Email (unique, indexed)
PasswordHash
DefaultRole (int)
IsActive (bool)
CreatedAt
UpdatedAt
```

**BoardAccesses**
```
Id (PK, GUID)
BoardId (FK, indexed)
UserId (FK, indexed)
Role (int)
GrantedBy (GUID)
GrantedAt
CreatedAt
UpdatedAt

Unique Index: (BoardId, UserId)
```

**Boards** (Updated)
```
... existing columns ...
OwnerId (GUID, nullable, indexed)
```

**AuditLogs**
```
Id (PK, GUID)
EntityType (indexed)
EntityId (indexed)
Action (int)
UserId (FK, nullable)
Changes (JSON)
Timestamp (indexed)
CreatedAt
UpdatedAt

Composite Index: (EntityType, EntityId)
```

**LlmRequests**
```
Id (PK, GUID)
UserId (FK, indexed)
BoardId (FK, nullable)
RequestType
Payload (text)
Status (int, indexed)
ErrorMessage
ProcessedAt
RetryCount
CreatedAt (indexed)
UpdatedAt

Composite Index: (UserId, Status)
```

## Implementation Checklist

### Phase 1: Core Authentication (Next Priority)
- [ ] Implement password hashing (BCrypt or Argon2)
- [ ] Create JWT token generation service
- [ ] Add JWT authentication middleware to API
- [ ] Create `UsersController` with register/login endpoints
- [ ] Add user context to requests (claim principal)
- [ ] Write authentication integration tests

### Phase 2: Authorization Enforcement
- [ ] Implement `AuthorizationService` with permission checks
- [ ] Add authorization middleware/attributes to controllers
- [ ] Update `BoardService` to check ownership/permissions
- [ ] Update `CardService` to enforce write permissions
- [ ] Update `ColumnService` to enforce write permissions
- [ ] Update `LabelService` to enforce write permissions
- [ ] Write authorization integration tests

### Phase 3: Board Access Management
- [ ] Implement `BoardAccessService`
- [ ] Create `BoardAccessController` (grant, revoke, list)
- [ ] Add UI for permission management
- [ ] Add CLI commands for access management
- [ ] Write board access integration tests

### Phase 4: Migration & Compatibility
- [ ] Create default "system" user for existing data
- [ ] Batch-assign ownership to default user
- [ ] Add backward compatibility mode (single-user)
- [ ] Update existing tests for multi-user scenarios

## Usage Examples (Future)

### Registering a User
```csharp
var result = await authService.RegisterAsync(new CreateUserDto(
    Username: "john.doe",
    Email: "john@example.com",
    Password: "SecureP@ssw0rd",
    DefaultRole: UserRole.Editor
));
```

### Granting Board Access
```csharp
var result = await boardAccessService.GrantAccessAsync(
    new GrantAccessDto(
        BoardId: boardId,
        UserId: userId,
        Role: UserRole.Editor
    ),
    grantedBy: currentUserId
);
```

### Checking Permissions
```csharp
var canWrite = await authService.CanWriteBoardAsync(userId, boardId);
if (!canWrite.Value)
{
    return Forbid();
}
```

## Security Considerations

1. **Password Storage:** Never store plain text passwords. Use BCrypt or Argon2.
2. **Token Security:** JWT tokens should have reasonable expiration times.
3. **Permission Checks:** Always validate permissions server-side, never trust client.
4. **SQL Injection:** EF Core provides protection; avoid raw SQL queries.
5. **Rate Limiting:** Implement rate limiting on authentication endpoints.

## Configuration

Add to `appsettings.json`:
```json
{
  "Authentication": {
    "JwtSecret": "your-secret-key-here-minimum-32-characters",
    "JwtIssuer": "Taskdeck",
    "JwtAudience": "TaskdeckUsers",
    "TokenExpirationMinutes": 60,
    "RefreshTokenExpirationDays": 30
  },
  "Authorization": {
    "EnableMultiUser": true,
    "DefaultOwnerRole": "Owner"
  }
}
```

## Testing Strategy

1. **Unit Tests:** Test domain entities, validation rules, permission logic
2. **Integration Tests:** Test authentication flow, authorization checks, API endpoints
3. **E2E Tests:** Test complete user journeys (register → login → create board → grant access)

## Next Steps

1. Review and approve architecture design
2. Implement JWT authentication service
3. Add authentication middleware to API
4. Create user management endpoints
5. Enforce authorization across existing endpoints
6. Add comprehensive tests

## References

- Domain Entities: `backend/src/Taskdeck.Domain/Entities/`
- Repository Interfaces: `backend/src/Taskdeck.Application/Interfaces/`
- Service Interfaces: `backend/src/Taskdeck.Application/Services/`
- Repository Implementations: `backend/src/Taskdeck.Infrastructure/Repositories/`
- Database Migration: `backend/src/Taskdeck.Infrastructure/Migrations/20260211082334_AddUserPermissionsAuditQueue.cs`
