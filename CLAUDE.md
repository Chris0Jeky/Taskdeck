# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Taskdeck is a personal Kanban and to-do manager with a Clean Architecture backend (.NET 8) and Vue 3 frontend. It uses an offline-first approach with SQLite for local storage.

## Development Commands

### Backend (.NET 8)

Navigate to `backend/` directory for all backend commands.

**Build and Run:**
```bash
cd backend
dotnet restore
dotnet run --project src/Taskdeck.Api/Taskdeck.Api.csproj
```

**Testing:**
```bash
cd backend
dotnet test                                    # Run all tests
dotnet test /p:CollectCoverage=true           # Run with coverage
```

**Database Migrations:**
```bash
cd backend
# Create migration
dotnet ef migrations add MigrationName -p src/Taskdeck.Infrastructure/Taskdeck.Infrastructure.csproj -s src/Taskdeck.Api/Taskdeck.Api.csproj

# Apply migrations
dotnet ef database update -p src/Taskdeck.Infrastructure/Taskdeck.Infrastructure.csproj -s src/Taskdeck.Api/Taskdeck.Api.csproj
```

### Frontend (Vue 3 + Vite)

Navigate to `frontend/taskdeck-web/` directory for all frontend commands.

```bash
cd frontend/taskdeck-web
npm install                 # Install dependencies
npm run dev                 # Start dev server (http://localhost:5173)
npm run build              # Build for production
npm run preview            # Preview production build
```

## Architecture

### Clean Architecture Layers

**Domain Layer** (`Taskdeck.Domain`)
- Core business entities: Board, Column, Card, Label, CardLabel
- Base `Entity` class with Id, CreatedAt, UpdatedAt tracking
- `Result` and `Result<T>` pattern for type-safe error handling
- Domain exceptions with typed error codes (NotFound, ValidationError, WipLimitExceeded, etc.)
- Rich domain models with encapsulated business logic and validation

**Application Layer** (`Taskdeck.Application`)
- Service classes: BoardService, CardService, ColumnService, LabelService
- DTOs for API contracts (separate from domain entities)
- Repository interfaces: IBoardRepository, ICardRepository, IColumnRepository, ILabelRepository
- `IUnitOfWork` interface for transaction management and repository access

**Infrastructure Layer** (`Taskdeck.Infrastructure`)
- `TaskdeckDbContext` - EF Core DbContext
- Repository implementations
- Entity configurations (Fluent API in `Persistence/Configurations/`)
- SQLite database provider

**API Layer** (`Taskdeck.Api`)
- ASP.NET Core REST controllers
- API runs on http://localhost:5000 by default
- Swagger documentation at /swagger in development mode

### Key Design Patterns

1. **Result Pattern**: All service methods return `Result` or `Result<T>` instead of throwing exceptions for business rule violations. Check `IsSuccess` and handle `ErrorCode`/`ErrorMessage`.

2. **Repository Pattern + Unit of Work**: Access repositories through `IUnitOfWork`. All repositories are exposed as properties (e.g., `_unitOfWork.Cards`, `_unitOfWork.Boards`). Call `SaveChangesAsync()` to persist changes.

3. **Domain Encapsulation**: Domain entities have private setters and expose behavior through methods (e.g., `card.Block(reason)`, `card.MoveToColumn(columnId, position)`). Never modify entity properties directly outside the entity.

4. **Navigation Property Management**: Entities have internal methods for managing collections (e.g., `Board.AddCard()`, `Card.AddLabel()`). These are called by infrastructure, not application services.

5. **Entity Base Class**: All entities inherit from `Entity` which provides Id, CreatedAt, UpdatedAt. Call `Touch()` to update timestamp.

### Frontend Architecture

**State Management**: Pinia store (`store/boardStore.ts`) manages all application state
- Centralized state for boards, cards, columns, labels
- Computed property `cardsByColumn` groups cards by column and sorts by position
- Actions handle API calls and state updates

**API Layer**:
- `api/http.ts` - Axios instance configured with base URL (defaults to http://localhost:5000/api)
- Environment variable: `VITE_API_BASE_URL` to override API endpoint
- Separate API modules: boardsApi, cardsApi, columnsApi, labelsApi

**Type Safety**: TypeScript types in `types/board.ts` mirror backend DTOs

## Business Rules to Respect

1. **WIP Limits**: Before moving/adding cards to a column, check `column.WouldExceedWipLimitIfAdded()`. Return `ErrorCodes.WipLimitExceeded` if violated.

2. **Position Management**:
   - Cards and columns maintain integer positions for ordering
   - When moving cards, reorder all affected cards in the target column
   - Use `card.SetPosition(i)` to update positions after reordering

3. **Validation**:
   - All entity constructors and methods validate inputs
   - Domain entities throw `DomainException` for validation failures
   - Service layer catches these and converts to Result.Failure

4. **Board Scoping**:
   - Cards belong to exactly one board and one column
   - Labels are board-scoped (each board has its own labels)
   - Verify board/column relationships before operations

5. **Label Associations**: Cards can have multiple labels via `CardLabel` join entity. Use `card.AddLabel(cardLabel)` and `card.ClearLabels()`.

## Error Handling

Always use the Result pattern in service methods:

```csharp
// Good
return Result.Failure<CardDto>(ErrorCodes.NotFound, $"Card with ID {id} not found");

// Bad - never throw exceptions from services
throw new NotFoundException($"Card {id} not found");
```

Standard error codes in `ErrorCodes` class:
- NotFound
- ValidationError
- WipLimitExceeded
- Conflict
- UnexpectedError

## Database

- SQLite database file: `taskdeck.db` (created in API project directory)
- Entity configurations use Fluent API (see `Infrastructure/Persistence/Configurations/`)
- Always use migrations for schema changes
- The infrastructure project references both Domain and Application projects

## Testing

- Test projects: `Taskdeck.Domain.Tests`, `Taskdeck.Application.Tests`
- Uses xUnit and FluentAssertions
- Focus on testing domain logic and service layer behavior
- Mock repositories using `IUnitOfWork` interface

## API Endpoint Patterns

All endpoints follow RESTful conventions:
- Board operations: `/api/boards/{id}`
- Nested resources: `/api/boards/{boardId}/cards`, `/api/boards/{boardId}/columns/{columnId}`
- Special operations: POST `/api/boards/{boardId}/cards/{cardId}/move`
- Use PATCH for partial updates, PUT for full updates

## Code Conventions

**Backend:**
- PascalCase for public members, camelCase with underscore prefix for private fields (`_field`)
- Use nullable reference types (`string?` for optional strings)
- Services inject `IUnitOfWork`, not individual repositories
- DTOs are records with positional parameters

**Frontend:**
- Vue 3 Composition API (not Options API)
- TypeScript strict mode enabled
- Pinia for state management (not Vuex)
- TailwindCSS for styling
