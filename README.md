# Taskdeck

**Taskdeck** is a personal Kanban and to-do manager designed for developers, featuring a keyboard-friendly interface, offline-first architecture, and clean design principles.

## 🎯 Features

- **Kanban Boards**: Visual management with boards → columns → cards
- **WIP Limits**: Enforce work-in-progress limits per column to maintain focus
- **Labels & Due Dates**: Organize cards with color-coded labels and track deadlines
- **Blocked Cards**: Mark cards as blocked with reasons to track impediments
- **Clean Architecture**: Backend built with Domain-Driven Design principles
- **Modern Stack**: Vue 3 + TypeScript frontend, .NET 8 + EF Core backend
- **Offline-First**: Local SQLite database, no cloud dependency required

## 📋 Tech Stack

### Backend
- **.NET 8** - Modern C# runtime
- **ASP.NET Core** - Web API framework
- **Entity Framework Core** - ORM with SQLite
- **Clean Architecture** - Domain, Application, Infrastructure, API layers
- **xUnit + FluentAssertions** - Testing framework

### Frontend
- **Vue 3** - Progressive JavaScript framework
- **Vite** - Fast build tool
- **TypeScript** - Type-safe JavaScript
- **Pinia** - State management
- **Vue Router** - Client-side routing
- **TailwindCSS** - Utility-first CSS framework
- **Axios** - HTTP client
- **Vitest + @vue/test-utils** - Testing framework

## 📚 Documentation

- **[IMPLEMENTATION_STATUS.md](./IMPLEMENTATION_STATUS.md)** - Complete project status, roadmap, and memory for development sessions
- **[TESTING.md](./TESTING.md)** - Comprehensive testing guide for backend and frontend
- **[TEST_SUITE_PLAN.md](./TEST_SUITE_PLAN.md)** - Testing strategy and test coverage plan
- **[CLAUDE.md](./CLAUDE.md)** - Development guidelines for Claude Code (AI coding assistant)
- **[Technical Design Document](./filesAndResources/taskdeck_technical_design_document.md)** - Original design specifications

## 🚀 Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/) and npm

### Backend Setup

1. Navigate to the backend directory:
```bash
cd backend
```

2. Restore dependencies:
```bash
dotnet restore
```

3. Create the database and run migrations:
```bash
dotnet ef database update -p src/Taskdeck.Infrastructure/Taskdeck.Infrastructure.csproj -s src/Taskdeck.Api/Taskdeck.Api.csproj
```

4. Run the API:
```bash
dotnet run --project src/Taskdeck.Api/Taskdeck.Api.csproj
```

The API will be available at `http://localhost:5000` (or the port specified in your configuration).

### Frontend Setup

1. Navigate to the frontend directory:
```bash
cd frontend/taskdeck-web
```

2. Install dependencies:
```bash
npm install
```

3. Start the development server:
```bash
npm run dev
```

The frontend will be available at `http://localhost:5173`.

## 🧪 Running Tests

### Backend Tests

Run all tests:
```bash
cd backend
dotnet test
```

Run tests with coverage:
```bash
dotnet test /p:CollectCoverage=true
```

**Current Status:**
- ✅ Domain Tests: 42/42 passing (100%)
- ✅ Application Tests: 87/87 passing (100%)
- ✅ Backend Total: 129/129 tests passing (100%)

### Frontend Tests

Run tests in watch mode:
```bash
cd frontend/taskdeck-web
npm run test
```

Run tests with coverage:
```bash
npm run test:coverage
```

Run tests with UI:
```bash
npm run test:ui
```

**Current Status:**
- ✅ Store Tests: 34/34 passing (100%) - boardStore + filtering logic
- ✅ Component Tests: 77/77 passing (100%) - All components
  - CardModal (12 tests)
  - BoardSettingsModal (12 tests)
  - ColumnEditModal (15 tests)
  - LabelManagerModal (17 tests)
  - FilterPanel (21 tests)
- ✅ Frontend Total: 111/111 tests passing (100%)

**Overall Test Status:**
- ✅ **240/240 tests passing (100%)**
  - Backend: 129 tests (Domain: 42, Application: 87)
  - Frontend: 111 tests (Store: 34, Components: 77)

See **[TESTING.md](./TESTING.md)** for comprehensive testing guide and best practices.

## 📐 Architecture

Taskdeck follows **Clean Architecture** principles with clear separation of concerns:

```
backend/
├── src/
│   ├── Taskdeck.Domain/         # Domain entities and business rules
│   │   ├── Entities/            # Board, Column, Card, Label
│   │   ├── Common/              # Base entity, Result pattern
│   │   └── Exceptions/          # Domain exceptions
│   │
│   ├── Taskdeck.Application/    # Use cases and business logic
│   │   ├── Services/            # BoardService, ColumnService, etc.
│   │   ├── DTOs/                # Data transfer objects
│   │   └── Interfaces/          # Repository interfaces
│   │
│   ├── Taskdeck.Infrastructure/ # Data access and external concerns
│   │   ├── Persistence/         # EF Core DbContext
│   │   └── Repositories/        # Repository implementations
│   │
│   └── Taskdeck.Api/            # REST API layer
│       └── Controllers/         # API endpoints
│
└── tests/
    ├── Taskdeck.Domain.Tests/
    └── Taskdeck.Application.Tests/
```

```
frontend/
└── taskdeck-web/
    └── src/
        ├── api/               # HTTP client and API calls
        ├── components/        # Vue components
        ├── router/            # Vue Router configuration
        ├── store/             # Pinia state management
        ├── types/             # TypeScript type definitions
        └── views/             # Page-level components
```

## 🎨 Domain Model

### Core Entities

**Board**
- Name, description
- Contains columns and cards
- Archive functionality

**Column**
- Name, position
- Optional WIP limit
- Belongs to a board

**Card**
- Title, description
- Due date (optional)
- Position within column
- Blocked status with reason
- Multiple labels

**Label**
- Name, color (hex)
- Board-scoped
- Many-to-many with cards

### Business Rules

1. **WIP Limit Enforcement**: Cards cannot be moved to a column that has reached its WIP limit
2. **Position Management**: Cards and columns maintain ordered positions
3. **Validation**: All entities enforce validation rules (e.g., non-empty names, valid hex colors)
4. **Board Integrity**: Cards must belong to exactly one board and one column

## 🔌 API Endpoints

### Boards
- `GET /api/boards` - List all boards
- `GET /api/boards/{id}` - Get board with columns
- `POST /api/boards` - Create a new board
- `PUT /api/boards/{id}` - Update board
- `DELETE /api/boards/{id}` - Archive board

### Columns
- `GET /api/boards/{boardId}/columns` - List columns for a board
- `POST /api/boards/{boardId}/columns` - Create a column
- `PATCH /api/boards/{boardId}/columns/{columnId}` - Update column
- `DELETE /api/boards/{boardId}/columns/{columnId}` - Delete column

### Cards
- `GET /api/boards/{boardId}/cards` - List/search cards
- `POST /api/boards/{boardId}/cards` - Create a card
- `PATCH /api/boards/{boardId}/cards/{cardId}` - Update card
- `POST /api/boards/{boardId}/cards/{cardId}/move` - Move card
- `DELETE /api/boards/{boardId}/cards/{cardId}` - Delete card

### Labels
- `GET /api/boards/{boardId}/labels` - List labels for a board
- `POST /api/boards/{boardId}/labels` - Create a label
- `PATCH /api/boards/{boardId}/labels/{labelId}` - Update label
- `DELETE /api/boards/{boardId}/labels/{labelId}` - Delete label

API documentation is available via Swagger at `http://localhost:5000/swagger` when running in development mode.

## 🗂️ Database

Taskdeck uses **SQLite** for local, file-based storage. The database file (`taskdeck.db`) is created in the API project directory on first run.

### Running Migrations

Create a new migration after model changes:
```bash
dotnet ef migrations add MigrationName -p src/Taskdeck.Infrastructure/Taskdeck.Infrastructure.csproj -s src/Taskdeck.Api/Taskdeck.Api.csproj
```

Apply migrations:
```bash
dotnet ef database update -p src/Taskdeck.Infrastructure/Taskdeck.Infrastructure.csproj -s src/Taskdeck.Api/Taskdeck.Api.csproj
```

## 🛠️ Development

### Code Style

- **Backend**: Follow standard C# conventions, use `PascalCase` for public members, `camelCase` for private fields
- **Frontend**: Use TypeScript strict mode, follow Vue 3 Composition API patterns

### Key Design Patterns

- **Repository Pattern**: Abstracts data access
- **Unit of Work**: Manages transactions
- **Result Pattern**: Type-safe error handling
- **Service Layer**: Encapsulates business logic
- **DTO Pattern**: Separates API contracts from domain models

## 📈 Roadmap

### ✅ Phase 1: Core Data Model & API (COMPLETED)
- ✅ Domain entities with validation (Board, Column, Card, Label)
- ✅ Clean Architecture implementation
- ✅ CRUD operations for all entities
- ✅ WIP limit enforcement
- ✅ Result pattern for error handling
- ✅ REST API with Swagger documentation
- ✅ 42 passing domain tests

### ✅ Phase 2: Basic Web UI (COMPLETED)
- ✅ Vue 3 + TypeScript + Pinia setup
- ✅ Boards list view
- ✅ Board view with columns and cards
- ✅ API integration layer
- ✅ Basic CRUD operations via UI

### ✅ Phase 3: UX Improvements (COMPLETED)
- ✅ Card modal for detailed editing
- ✅ Board settings modal
- ✅ Column management modal
- ✅ Label manager modal
- ✅ Complete CRUD operations for all entities
- ✅ Frontend tests with Vitest

### ✅ Phase 4: UX Enhancements (COMPLETED)
- ✅ Toast notification system
- ✅ Drag-and-drop for cards
- ✅ Drag-and-drop for columns
- ✅ Visual feedback for all operations
- ✅ Atomic column reordering with two-phase updates

### ✅ Phase 5: Enhanced UX & Accessibility (COMPLETED)
- ✅ Keyboard shortcuts (navigation and operations)
- ✅ Advanced filtering UI
- ✅ Help modal and documentation

### 🚧 Phase 6: Advanced Features (NEXT)
- [ ] Time tracking per card
- [ ] CLI client
- [ ] Recurring tasks
- [ ] Analytics dashboard
- [ ] Dark mode
- [ ] Multi-user support (optional)
- [ ] Sync to remote server (optional)

**Detailed roadmap:** See [IMPLEMENTATION_STATUS.md](./IMPLEMENTATION_STATUS.md)

## 🤝 Contributing

This is primarily a personal learning project, but feedback and suggestions are welcome!

## 📄 License

MIT License - feel free to use this project as a reference or starting point for your own Kanban tool.

## 🙏 Acknowledgments

- Inspired by Trello, Jira, and other Kanban tools
- Built following Clean Architecture principles by Robert C. Martin
- Uses modern best practices for .NET and Vue.js development

---

**Happy task tracking!** 🎯
