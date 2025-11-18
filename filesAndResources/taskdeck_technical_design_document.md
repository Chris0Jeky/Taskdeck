# Taskdeck – Technical & Product Design Document

## 0. Overview

**Name:** Taskdeck  
**Tagline:** Personal Kanban and to‑do manager for developers – keyboard‑friendly, offline‑first, extensible.

Taskdeck is a single‑user‑first Kanban and to‑do application designed primarily for developers, with a strong emphasis on:

- Fast, low‑friction capture of tasks while coding (CLI + keyboard‑centric UI).
- Visual management of work using boards, columns, and cards.
- WIP (Work In Progress) limits to enforce focus and flow.
- Offline‑first local usage with a clear path to sync and multi‑user support later.

This document describes the **product vision**, **requirements**, **architecture**, **stack**, **API**, **frontend design**, **CLI design**, and **roadmap**.

---

## 1. Vision & Goals

### 1.1 Problem

Most mainstream tools (Trello, Jira, Asana, etc.) are either:

- Overkill for a single developer or small personal projects, or
- Too mouse‑centric / slow to interact with while deep in code.

Developers often want something that:

- Lives on their machine.
- Is extremely fast to use.
- Can be automated / scripted.
- Respects Kanban principles like limiting WIP.

### 1.2 High‑Level Goals

1. **Personal productivity first**
   - Single user, local DB storage, fast startup.
2. **Developer‑centric UX**
   - CLI for quick actions.
   - Web UI with strong keyboard support.
3. **Kanban done right**
   - Boards → Columns → Cards.
   - WIP limits, flow metrics later.
4. **Extensible architecture**
   - Clean separation of domain, application, infrastructure.
   - Able to add multi‑user, syncing, time‑tracking, integrations later.

### 1.3 Non‑Goals (for MVP)

- Team collaboration (no multi‑user auth, no real‑time sync).
- Complex permissions or roles.
- Advanced automation/integration (webhooks, third‑party APIs).

These may be added later but should not complicate the MVP.

---

## 2. Feature Overview

### 2.1 MVP Feature Set

- Boards
  - Create, list, update (rename/description), archive.

- Columns
  - Create columns for a board.
  - Reorder columns.
  - Optional WIP limit per column.

- Cards
  - Create cards (title, description, optional due date, labels).
  - Move cards between columns.
  - Reorder cards within a column.
  - Mark card as blocked/unblocked with a reason.

- Labels
  - Board‑scoped labels with name + color.
  - Assign labels to cards.

- Filtering/search
  - Filter cards by text and label.

- Web UI
  - Boards list screen.
  - Board view with columns and cards.

- Backend API
  - REST API for all above operations.

### 2.2 Near‑Term Features

- Time tracking per card (manual start/stop or quick estimation).
- Recurring tasks.
- Basic analytics (cards completed per week, WIP trend).
- Keyboard shortcuts for all main actions.
- Drag & drop for columns and cards.

### 2.3 Long‑Term Ideas

- Multi‑user support and shared boards.
- Sync to remote server / self‑hosted instance.
- Integrations:
  - Git repositories (link cards to branches, commits, or files).
  - Developer Swiss‑army‑knife utilities (e.g., open a dev tool from a card).
- Mobile‑friendly web/PWA.

---

## 3. System Architecture Overview

### 3.1 Architectural Style

Taskdeck uses a **layered Clean Architecture** style:

- **Domain** – Entities, value objects, domain rules.
- **Application** – Use cases (commands/queries), DTOs, interfaces.
- **Infrastructure** – Database (EF Core + SQLite), repository implementations.
- **API (Presentation)** – ASP.NET Core Web API, request/response mapping.
- **Web Frontend** – Vue 3 SPA (TypeScript) consuming the API.
- **CLI** – Later: .NET console app depending on Application/Infrastructure.

### 3.2 High‑Level Component Diagram (Conceptual)

- `Taskdeck.Web (Vue SPA)`  →  HTTP  →  `Taskdeck.Api (ASP.NET Core)`
- `Taskdeck.Cli (.NET console)`  →  directly uses `Taskdeck.Application`
- `Taskdeck.Api`  →  `Taskdeck.Application` → `Taskdeck.Domain`
- `Taskdeck.Application`  →  abstractions (repositories)
- `Taskdeck.Infrastructure`  →  implements repositories, EF Core context, SQLite

### 3.3 Cross‑Cutting Concerns

- Logging & diagnostics.
- Validation (request validation, domain invariants).
- Error handling and consistent error responses.
- Configuration management.
- Testing strategy.

---

## 4. Tech Stack Decisions & Rationale

### 4.1 Backend

- **Language**: C#
- **Runtime**: .NET 8
- **Framework**: ASP.NET Core Web API
- **ORM**: Entity Framework Core
- **Database**: SQLite (file‑based, ideal for local personal usage)

**Why:**

- You have experience with C# and Java; C#/.NET is very productive for APIs and CLIs.
- ASP.NET Core is fast, cross‑platform, and integrates well with EF Core.
- SQLite gives you a simple local file DB, no server dependency.
- Clean Architecture is a good fit for future growth (multi‑user, sync, CLI etc.).

### 4.2 Frontend

- **Framework**: Vue 3
- **Build**: Vite
- **Language**: TypeScript
- **State Management**: Pinia
- **Routing**: Vue Router
- **Styling**: TailwindCSS

**Why:**

- Vue 3 + Vite gives a fast dev experience and a simple mental model.
- Pinia is the modern, clean state management library for Vue.
- Typed frontend with TypeScript increases robustness.
- TailwindCSS accelerates layout/UX iterations.

### 4.3 CLI (Future)

- **Language**: C# (.NET console app)
- **Libraries**: System.CommandLine or Spectre.Console
- Reuse Application and Infrastructure layers to ensure identical behavior to API.

### 4.4 Desktop (Future)

Two main options:

1. Ship the Vue SPA as a desktop app using **Tauri** or **Electron**, pointing to local API.
2. Use **Avalonia UI** for a native cross‑platform .NET desktop app.

Initially, the focus is on Web + CLI; desktop comes later.

---

## 5. Repository & Folder Structure

Top‑level layout:

```text
taskdeck/
  README.md
  .gitignore
  .editorconfig

  backend/
    Taskdeck.sln
    src/
      Taskdeck.Domain/
      Taskdeck.Application/
      Taskdeck.Infrastructure/
      Taskdeck.Api/
    tests/
      Taskdeck.Domain.Tests/
      Taskdeck.Application.Tests/

  frontend/
    taskdeck-web/
      (Vite + Vue + TS project)
```

### 5.1 Backend Projects

**Taskdeck.Domain**

- Entities: `Board`, `Column`, `Card`, `Label`, `CardLabel` (join entity).
- Value objects (if needed): e.g., `ColumnName`, `BoardName`.
- Domain services (if any complex invariants appear).
- No dependencies on other projects.

**Taskdeck.Application**

- Use cases/services: commands and queries.
  - `CreateBoardCommand`, `GetBoardByIdQuery`, `CreateCardCommand`, `MoveCardCommand`, etc.
- Interfaces for repositories: `IBoardRepository`, `IColumnRepository`, `ICardRepository`, `ILabelRepository`.
- DTOs / view models used by the API.
- Validation logic.
- References `Taskdeck.Domain` only.

**Taskdeck.Infrastructure**

- EF Core `TaskdeckDbContext`.
- Entity configurations (Fluent API) for each entity.
- Repository implementations.
- Migration setup.
- References `Taskdeck.Domain` and `Taskdeck.Application`.

**Taskdeck.Api**

- ASP.NET Core startup/Program.
- Controller or minimal API endpoints.
- Request/response models (mapped to/from Application DTOs).
- Dependency injection wiring for Application and Infrastructure.
- References `Taskdeck.Application` and `Taskdeck.Infrastructure`.

### 5.2 Frontend Project Structure

Inside `frontend/taskdeck-web`:

```text
src/
  main.ts
  App.vue

  router/
    index.ts

  store/
    boardsStore.ts
    uiStore.ts

  api/
    http.ts
    boardsApi.ts
    columnsApi.ts
    cardsApi.ts
    labelsApi.ts

  components/
    layout/
      AppShell.vue
      SidebarBoards.vue

    board/
      BoardView.vue
      ColumnLane.vue
      CardItem.vue
      CardModal.vue
      NewColumnForm.vue
      NewCardForm.vue

  views/
    BoardsListView.vue
    BoardViewPage.vue

  types/
    board.ts
    card.ts
    column.ts
    label.ts

  assets/
    (logos, icons, etc.)
```

---

## 6. Domain Model

### 6.1 Entities

#### Board

- `Guid Id`
- `string Name`
- `string? Description`
- `DateTimeOffset CreatedAt`
- `DateTimeOffset UpdatedAt`
- `bool IsArchived`

#### Column

- `Guid Id`
- `Guid BoardId`
- `string Name`
- `int Position` (0‑based index within board)
- `int? WipLimit` (null = no limit)
- `DateTimeOffset CreatedAt`
- `DateTimeOffset UpdatedAt`

#### Card

- `Guid Id`
- `Guid BoardId`
- `Guid ColumnId`
- `string Title`
- `string Description` (Markdown)
- `DateTimeOffset? DueDate`
- `bool IsBlocked`
- `string? BlockReason`
- `int Position` (0‑based index within column)
- `DateTimeOffset CreatedAt`
- `DateTimeOffset UpdatedAt`

#### Label

- `Guid Id`
- `Guid BoardId`
- `string Name`
- `string ColorHex` (e.g. `#EF4444`)

#### CardLabel (Join Entity)

- `Guid CardId`
- `Guid LabelId`

### 6.2 Relationships

- One `Board` has many `Columns`.
- One `Board` has many `Cards`.
- One `Column` has many `Cards`.
- Many‑to‑many between `Card` and `Label`.

### 6.3 Domain Invariants & Rules

1. A `Column`’s `Position` must be unique per board.
2. A `Card` must belong to exactly one `Column` and one `Board`, and the `BoardId` must match its column’s `BoardId`.
3. If `Column.WipLimit` is set, the number of non‑archived cards in that column must not exceed the limit.
4. `Board.Name` and `Column.Name` should be non‑empty and reasonably short.
5. `ColorHex` for labels should be a valid 7‑character string `#RRGGBB`.

### 6.4 Future Domain Extensions

- `User` entity and `BoardMember` join entity for multi‑user.
- `ActivityLog` per card.
- `ChecklistItem` for subtasks on a card.
- `Attachment` (linked files, URLs).
- `CardEstimate` and time‑tracking sessions.

---

## 7. Persistence Model

### 7.1 SQLite Schema (Conceptual)

Tables (simplified):

- `Boards`
  - `Id (TEXT, PK)`
  - `Name (TEXT)`
  - `Description (TEXT)`
  - `IsArchived (INTEGER)`
  - `CreatedAt (TEXT)`
  - `UpdatedAt (TEXT)`

- `Columns`
  - `Id (TEXT, PK)`
  - `BoardId (TEXT, FK Boards.Id)`
  - `Name (TEXT)`
  - `Position (INTEGER)`
  - `WipLimit (INTEGER, NULLABLE)`
  - `CreatedAt (TEXT)`
  - `UpdatedAt (TEXT)`

- `Cards`
  - `Id (TEXT, PK)`
  - `BoardId (TEXT, FK Boards.Id)`
  - `ColumnId (TEXT, FK Columns.Id)`
  - `Title (TEXT)`
  - `Description (TEXT)`
  - `DueDate (TEXT, NULLABLE)`
  - `IsBlocked (INTEGER)`
  - `BlockReason (TEXT, NULLABLE)`
  - `Position (INTEGER)`
  - `CreatedAt (TEXT)`
  - `UpdatedAt (TEXT)`

- `Labels`
  - `Id (TEXT, PK)`
  - `BoardId (TEXT, FK Boards.Id)`
  - `Name (TEXT)`
  - `ColorHex (TEXT)`

- `CardLabels`
  - `CardId (TEXT, FK Cards.Id)`
  - `LabelId (TEXT, FK Labels.Id)`
  - PK on `(CardId, LabelId)`

### 7.2 EF Core Configuration

- Use Fluent API in `OnModelCreating` to:
  - Configure relationships and cascade behavior.
  - Configure composite key on `CardLabels`.
  - Set default values for `CreatedAt`/`UpdatedAt`.
  - Enforce required fields.

### 7.3 Migrations

- Use EF Core migrations to evolve schema.
- Naming convention: `YYYYMMDDHHMM_Description`.

---

## 8. Application Layer (Use Cases)

The Application layer exposes use cases as Services or Command/Query handlers.

### 8.1 Board Use Cases

- `CreateBoardCommand`
  - Input: Name, optional Description.
  - Output: Board DTO.

- `UpdateBoardCommand`
  - Input: BoardId, Name?, Description?, IsArchived?

- `GetBoardByIdQuery`
  - Input: BoardId.
  - Output: Board detail DTO (optionally including columns).

- `ListBoardsQuery`
  - Input: optional filters (Search text, archived flag).
  - Output: collection of Board summary DTOs.

### 8.2 Column Use Cases

- `CreateColumnCommand`
  - Input: BoardId, Name, Position?, WipLimit?

- `UpdateColumnCommand`
  - Input: ColumnId, Name?, WipLimit?, Position?.

- `DeleteColumnCommand`
  - Input: ColumnId.
  - Behavior: check or handle cards in column (either disallow or move).

- `ReorderColumnsCommand`
  - Input: BoardId, ordered list of ColumnIds.

### 8.3 Card Use Cases

- `CreateCardCommand`
  - Input: BoardId, ColumnId, Title, Description?, DueDate?, LabelIds?

- `UpdateCardCommand`
  - Input: CardId, Title?, Description?, DueDate?, IsBlocked?, BlockReason?, LabelIds?

- `MoveCardCommand`
  - Input: CardId, TargetColumnId, TargetPosition.
  - Behavior: enforce WIP limits, update positions.

- `DeleteCardCommand`
  - Input: CardId.

- `ListCardsQuery`
  - Input: BoardId, optional ColumnId, SearchText, LabelId.
  - Output: list of card DTOs.

### 8.4 Label Use Cases

- `CreateLabelCommand`
  - Input: BoardId, Name, ColorHex.

- `UpdateLabelCommand`
  - Input: LabelId, Name?, ColorHex?.

- `DeleteLabelCommand`
  - Input: LabelId.

- `ListLabelsQuery`
  - Input: BoardId.

### 8.5 Validation & Error Handling Patterns

- Validate all input DTOs using either:
  - Manual validation in handlers, or
  - A validator library (e.g., FluentValidation) in Application layer.

- Application errors should be represented as well‑typed result objects, for example:
  - `Result<T>` with `IsSuccess`, `ErrorCode`, `ErrorMessage`.

Typical error codes:

- `NotFound` (board/column/card/label not found).
- `ValidationError` (invalid field values).
- `WipLimitExceeded` (trying to move or create card beyond column limit).
- `Conflict` (e.g., duplicate name constraints, concurrency issues).

---

## 9. API Design

### 9.1 Conventions

- Base path: `/api`.
- JSON everywhere.
- REST‑style, resource‑based endpoints.
- Use plural resource names: `/boards`, `/columns`, `/cards`, `/labels`.
- For nested relationships, use `/boards/{boardId}/columns` etc.

### 9.2 Board Endpoints

**List Boards**

`GET /api/boards`

Query parameters (optional):

- `search`: filter by name substring.
- `includeArchived`: `true/false` (default false).

Example response:

```json
[
  {
    "id": "b6de...",
    "name": "Personal",
    "description": "Personal tasks",
    "isArchived": false
  }
]
```

**Create Board**

`POST /api/boards`

Request:

```json
{
  "name": "Personal",
  "description": "Personal tasks and projects"
}
```

Response:

```json
{
  "id": "b6de...",
  "name": "Personal",
  "description": "Personal tasks and projects",
  "isArchived": false
}
```

**Get Board**

`GET /api/boards/{boardId}`

Return board details and optionally columns (MVP: include columns).

Example response:

```json
{
  "id": "b6de...",
  "name": "Personal",
  "description": "Personal tasks and projects",
  "isArchived": false,
  "columns": [
    {
      "id": "c1...",
      "name": "To Do",
      "position": 0,
      "wipLimit": 5
    }
  ]
}
```

**Update Board**

`PUT /api/boards/{boardId}`

```json
{
  "name": "Personal (Home)",
  "description": "Home & personal tasks",
  "isArchived": false
}
```

**Archive Board**

`DELETE /api/boards/{boardId}`

- Soft delete: sets `IsArchived = true`.

### 9.3 Column Endpoints

**List Columns for a Board**

`GET /api/boards/{boardId}/columns`

**Create Column**

`POST /api/boards/{boardId}/columns`

```json
{
  "name": "In Progress",
  "position": 1,
  "wipLimit": 3
}
```

**Update Column**

`PATCH /api/boards/{boardId}/columns/{columnId}`

```json
{
  "name": "Doing",
  "wipLimit": 4,
  "position": 1
}
```

**Delete Column**

`DELETE /api/boards/{boardId}/columns/{columnId}`

Behavior: MVP may disallow deletion if column has cards, or require explicit `force` flag.

### 9.4 Card Endpoints

**List Cards**

`GET /api/boards/{boardId}/cards`

Query parameters:

- `columnId` (optional)
- `search` (optional)
- `labelId` (optional)

**Create Card**

`POST /api/boards/{boardId}/cards`

```json
{
  "columnId": "c1...",
  "title": "Finish boids refactor",
  "description": "Refactor simulation core.",
  "dueDate": "2025-12-01T18:00:00Z",
  "labelIds": ["l1...", "l2..."]
}
```

**Get Card**

`GET /api/boards/{boardId}/cards/{cardId}`

**Update Card**

`PATCH /api/boards/{boardId}/cards/{cardId}`

```json
{
  "title": "Finish boids refactor (v2)",
  "description": "Refactor core and add tests.",
  "dueDate": "2025-12-03T12:00:00Z",
  "isBlocked": true,
  "blockReason": "Waiting on profiling results",
  "labelIds": ["l1..."]
}
```

**Move Card**

`POST /api/boards/{boardId}/cards/{cardId}/move`

```json
{
  "targetColumnId": "c2...",
  "targetPosition": 0
}
```

Response: updated card DTO, or error with `WipLimitExceeded`.

**Delete Card**

`DELETE /api/boards/{boardId}/cards/{cardId}`

### 9.5 Label Endpoints

**List Labels**

`GET /api/boards/{boardId}/labels`

**Create Label**

`POST /api/boards/{boardId}/labels`

```json
{
  "name": "Important",
  "colorHex": "#EF4444"
}
```

**Update Label**

`PATCH /api/boards/{boardId}/labels/{labelId}`

```json
{
  "name": "High Priority",
  "colorHex": "#DC2626"
}
```

**Delete Label**

`DELETE /api/boards/{boardId}/labels/{labelId}`

---

## 10. Frontend Application Design

### 10.1 UX Goals

- Minimal friction:
  - Quickly switch boards.
  - Quickly add/edit cards.
  - Keyboard shortcuts for common actions.
- Visual clarity:
  - Column layout with clear WIP indicators.
  - Clear label colors and due‑date indicators.
- Responsive enough to work on laptop screens comfortably.

### 10.2 Screen Inventory

1. **Boards List View** (`/boards`)
   - List of boards (name, number of cards, archived badge).
   - Button to create a new board.

2. **Board View** (`/boards/:id`)
   - Columns displayed horizontally.
   - Cards listed within columns.
   - Filter bar for search and label filtering.
   - WIP limit and card count shown per column.

### 10.3 Navigation Structure

- Root route `/` redirects to `/boards`.
- `/boards` – BoardsListView.
- `/boards/:id` – BoardViewPage.

### 10.4 State Management (Pinia)

**boardsStore**

State:

- `boards: BoardSummary[]`
- `currentBoard: BoardDetail | null`

Actions:

- `fetchBoards()`
- `createBoard(payload)`
- `fetchBoardById(boardId)`
- `updateBoard(boardId, payload)`

**uiStore**

State:

- `isLoading: boolean`
- `errorMessage: string | null`
- `activeBoardId: string | null`
- `boardFilters: { search: string; labelId?: string }`

Actions:

- `setLoading(bool)`
- `setError(msg)`
- `setBoardFilters(filters)`

Future stores (or feature stores): `cardsStore`, `labelsStore`, or embed them into `boardsStore` as nested data.

### 10.5 API Integration

Create a base Axios instance in `api/http.ts`:

- Base URL from `VITE_API_BASE_URL`.
- Response interceptors for error handling.

Feature‑specific clients:

- `boardsApi.ts` – CRUD for boards.
- `columnsApi.ts` – CRUD for columns.
- `cardsApi.ts` – CRUD, move for cards.
- `labelsApi.ts` – CRUD for labels.

Components and stores call these API modules, not Axios directly.

### 10.6 Components

**AppShell.vue**

- Layout skeleton: top bar + main content.
- Slot for views.

**SidebarBoards.vue**

- Displays list of boards.
- Allows quick switching.

**BoardsListView.vue**

- Uses `boardsStore.fetchBoards()`.
- Displays board cards with name/description.
- “New board” button opens a simple form.

**BoardViewPage.vue**

- Fetches board details, columns, and cards.
- Renders `BoardView` component.
- Provides filter bar.

**BoardView.vue**

- Receives `board`, `columns`, `cards` as props.
- Renders `ColumnLane` for each column, horizontally scrollable.

**ColumnLane.vue**

- Props: column, cards belonging to that column.
- Shows column name, WIP limit, count.
- Renders list of `CardItem`.
- “Add card” button.

**CardItem.vue**

- Props: card, labels.
- Compact representation: title, due date, label chips.
- Click opens `CardModal`.

**CardModal.vue**

- Edit title, description (textarea), due date.
- Toggle blocked state + reason.
- Label selection via checkboxes/chips.

### 10.7 Styling & Theme

- TailwindCSS for utilities.
- Light theme initially:
  - Neutral background.
  - Cards with subtle shadow and rounded corners.
  - Column headers with slightly stronger background.
- Later: toggle for dark mode.

---

## 11. CLI Client Design (Future)

### 11.1 Goals

- Enable quick, scriptable interactions from terminal.
- Reuse Application services to remain consistent with Web UI.

### 11.2 Commands (Draft)

Examples:

```bash
# Boards
taskdeck boards list
taskdeck board create "Personal"

# Columns
taskdeck columns list --board "Personal"

# Cards
taskdeck cards list --board "Personal" --column "To Do"

# Add card quickly
taskdeck card add "Fix bug #123" --board "Personal" --column "To Do" --labels "Bug,Important"

# Move card
taskdeck card move 8a2f... --column "Doing"
```

### 11.3 Architecture

- Project: `Taskdeck.Cli` in `backend/src/Taskdeck.Cli`.
- Reference `Taskdeck.Application` and `Taskdeck.Infrastructure`.
- Bootstraps DI container similarly to `Taskdeck.Api` but without HTTP.
- Maps CLI arguments to Application commands/queries.

---

## 12. Cross‑Cutting Concerns

### 12.1 Logging

- Use built‑in ASP.NET logging.
- Log request start/end with board/card IDs as needed.
- Infrastructure logs DB exceptions.
- For CLI, log to console with minimal, clear messages.

### 12.2 Configuration

- `appsettings.json` for API settings.
- Separate `appsettings.Development.json` for dev environment.
- `VITE_API_BASE_URL` in frontend `.env`.

### 12.3 Error Handling & Responses

API error response shape:

```json
{
  "errorCode": "WipLimitExceeded",
  "message": "Cannot move card, target column has reached its WIP limit.",
  "details": null
}
```

Standard error codes:

- `NotFound`
- `ValidationError`
- `WipLimitExceeded`
- `Conflict`
- `UnexpectedError`

### 12.4 Internationalization (Future)

- Frontend: route display strings through a simple i18n layer.
- Backend: return error codes + English messages, allow UI to localize.

---

## 13. Testing Strategy

### 13.1 Backend Tests

**Domain Tests (Taskdeck.Domain.Tests)**

- Test domain rules and invariants:
  - WIP limitation logic.
  - Card move rules.
  - Column position behavior.

**Application Tests (Taskdeck.Application.Tests)**

- Test use case handlers:
  - `CreateBoardCommandHandler`.
  - `MoveCardCommandHandler` (including WIP limit failures).

**Integration Tests (Optional)**

- Use ASP.NET Core `WebApplicationFactory` to test full API endpoints against a test SQLite or in‑memory DB.

### 13.2 Frontend Tests

- Unit tests with **Vitest** for components:
  - BoardView, ColumnLane, CardItem interactions.
- E2E tests with **Cypress** or **Playwright**:
  - Basic flow: create board → add columns → add cards → move cards.

---

## 14. Deployment & Environments

### 14.1 Local Development

Backend:

- `dotnet run` from `Taskdeck.Api`.
- SQLite DB stored as a local file (`taskdeck.db`).

Frontend:

- `npm run dev` from `frontend/taskdeck-web`.
- Vite dev server proxies API requests to backend.

### 14.2 Dockerization (Optional Early)

- Dockerfile for API.
- Dockerfile for frontend (or a combined Nginx container serving built SPA and proxying to API).

### 14.3 Hosting Options (Later)

- Host API on a small VPS or PaaS (Fly.io, Render, etc.).
- Host frontend as static files (Netlify, Vercel, etc.).
- For a personal tool, local hosting may be sufficient.

---

## 15. Roadmap & Milestones

### Phase 1 – Core Data Model & API

- Implement domain entities and EF Core mappings.
- Implement board/column/card/label use cases.
- Expose basic CRUD endpoints.
- Verify WIP limit logic via tests.

### Phase 2 – Basic Web UI

- Scaffold Vue 3 + Vite + Tailwind.
- Implement BoardsListView.
- Implement BoardViewPage with columns and cards list.
- Hook up API integration.

### Phase 3 – UX Improvements

- Add card modal for editing.
- Filtering by text and labels.
- Better error and loading states.
- Setup keyboard shortcuts for common actions.

### Phase 4 – Advanced Features

- Drag‑and‑drop for columns and cards.
- Time tracking and basic analytics.
- CLI client.
- Optional sync/multi‑user.

---

## 16. Coding Guidelines & Conventions

### 16.1 C# Backend

- Use `PascalCase` for classes and methods, `camelCase` for local variables.
- Avoid anemic domain objects whenever business logic is non‑trivial.
- Keep controllers thin; push logic into Application layer.
- Group files by feature within each layer where it makes sense (e.g., `Boards`, `Columns`, `Cards`).

### 16.2 TypeScript/Vue Frontend

- Use `<script setup lang="ts">` in Vue components.
- Use strongly typed props and emits.
- Keep presentational components dumb; business logic in stores/composables.

### 16.3 Git Workflow

- Main branch: `main`.
- Feature branches: `feature/<short-description>`.
- Conventional commit messages are nice but not mandatory.

---

## 17. Initial README Skeleton

You can base the repo README on something like this (adapted):

```markdown
# Taskdeck

Taskdeck is a personal Kanban and to-do manager for developers.

- Boards → Columns → Cards
- WIP limits per column
- Keyboard-friendly web UI
- Local SQLite database

## Tech Stack

**Backend**

- .NET 8 (C#)
- ASP.NET Core Web API
- Entity Framework Core + SQLite

**Frontend**

- Vue 3 + Vite
- TypeScript
- Pinia
- TailwindCSS

## Getting Started

### Prerequisites

- .NET 8 SDK
- Node.js 20+ and npm

### Backend

```bash
cd backend
# Run migrations (if any) and start API
dotnet ef database update -p src/Taskdeck.Infrastructure/Taskdeck.Infrastructure.csproj -s src/Taskdeck.Api/Taskdeck.Api.csproj

dotnet run --project src/Taskdeck.Api/Taskdeck.Api.csproj
```

The API runs on `http://localhost:5000` by default (configurable).

### Frontend

```bash
cd frontend/taskdeck-web
npm install
npm run dev
```

The app will be available on `http://localhost:5173`.

Set `VITE_API_BASE_URL` in `.env` if needed.

## Roadmap

- Boards/Columns/Cards/Labels CRUD
- WIP limits enforcement
- Card filters
- Drag-and-drop for cards and columns
- CLI client
- Basic analytics

---

Taskdeck is primarily a learning and personal productivity project, but the architecture is designed to grow.
```

---

This document should be treated as a living design artifact; as you implement features, you can adjust decisions, add diagrams, and refine sections to match the actual codebase.

