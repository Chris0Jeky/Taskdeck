# Taskdeck - Comprehensive Test Suite Plan

**Version:** 1.0
**Last Updated:** 2025-11-18
**Status:** Planning Phase

This document defines the complete testing strategy for Taskdeck, including unit tests, integration tests, and end-to-end tests across all layers.

---

## Table of Contents

1. [Testing Philosophy](#testing-philosophy)
2. [Test Coverage Goals](#test-coverage-goals)
3. [Backend Testing](#backend-testing)
4. [Frontend Testing](#frontend-testing)
5. [Integration Testing](#integration-testing)
6. [Test Data Strategy](#test-data-strategy)
7. [CI/CD Integration](#cicd-integration)
8. [Test Priorities](#test-priorities)

---

## Testing Philosophy

### Principles

1. **Test Behavior, Not Implementation** - Focus on what the code does, not how it does it
2. **Fast Feedback** - Unit tests should run in milliseconds
3. **Isolation** - Each test should be independent and not rely on others
4. **Clarity** - Test names should clearly describe what is being tested
5. **Maintainability** - Tests should be easy to understand and update
6. **Pyramid Approach** - Many unit tests, fewer integration tests, few E2E tests

### Test Pyramid

```
       /\
      /  \     E2E Tests (5-10 key user flows)
     /____\
    /      \   Integration Tests (API + DB, Component + Store)
   /________\
  /          \ Unit Tests (Domain logic, Services, Components)
 /__________\
```

### Coverage Goals

- **Domain Layer:** 90%+ coverage (critical business logic)
- **Application Layer:** 85%+ coverage (use cases and services)
- **API Layer:** 75%+ coverage (controllers and middleware)
- **Frontend Components:** 70%+ coverage (UI components)
- **E2E:** Critical user journeys covered

---

## Backend Testing

### Domain Layer Tests (`Taskdeck.Domain.Tests`)

**Status:** ✅ 42 tests passing

#### Existing Tests (Keep and Expand)

**Board Entity Tests:**
- ✅ Constructor creates board with valid data
- ✅ Constructor throws on empty name
- ✅ Constructor throws on name too long
- ✅ Update changes name
- ✅ Update changes description
- ✅ Archive sets IsArchived to true
- ✅ Unarchive sets IsArchived to false

**Card Entity Tests:**
- ✅ Constructor creates card with valid data
- ✅ Constructor throws on empty title
- ✅ Constructor throws on title too long
- ✅ Update changes fields
- ✅ Block sets blocked state
- ✅ Block throws when reason is empty
- ✅ Unblock clears blocked state

**Column Entity Tests:**
- ✅ (Assumed similar coverage based on test count)

**Label Entity Tests:**
- ✅ (Assumed similar coverage based on test count)

#### Tests to Add

**Card Entity - Label Management:**
```csharp
[Fact]
public void AddLabel_ShouldAddLabelToCollection()
{
    // Arrange
    var card = new Card(boardId, columnId, "Test Card");
    var cardLabel = new CardLabel(card.Id, labelId);

    // Act
    card.AddLabel(cardLabel);

    // Assert
    card.CardLabels.Should().ContainSingle();
    card.CardLabels.First().Should().Be(cardLabel);
}

[Fact]
public void ClearLabels_ShouldRemoveAllLabels()
{
    // Arrange
    var card = new Card(boardId, columnId, "Test Card");
    card.AddLabel(new CardLabel(card.Id, labelId1));
    card.AddLabel(new CardLabel(card.Id, labelId2));

    // Act
    card.ClearLabels();

    // Assert
    card.CardLabels.Should().BeEmpty();
}

[Fact]
public void RemoveLabel_ShouldRemoveSpecificLabel()
{
    // Arrange
    var card = new Card(boardId, columnId, "Test Card");
    var cardLabel1 = new CardLabel(card.Id, labelId1);
    var cardLabel2 = new CardLabel(card.Id, labelId2);
    card.AddLabel(cardLabel1);
    card.AddLabel(cardLabel2);

    // Act
    card.RemoveLabel(cardLabel1);

    // Assert
    card.CardLabels.Should().ContainSingle();
    card.CardLabels.Should().Contain(cardLabel2);
}
```

**Card Entity - Position and Movement:**
```csharp
[Fact]
public void SetPosition_ShouldUpdatePosition()
{
    // Test position updates
}

[Fact]
public void SetPosition_ShouldThrowOnNegativePosition()
{
    // Test validation
}

[Fact]
public void MoveToColumn_ShouldUpdateColumnIdAndPosition()
{
    // Test column movement
}

[Fact]
public void MoveToColumn_ShouldUpdateTimestamp()
{
    // Test Touch() is called
}
```

**Column Entity - WIP Limit:**
```csharp
[Fact]
public void WouldExceedWipLimitIfAdded_ShouldReturnTrue_WhenAtLimit()
{
    // Test WIP limit detection
}

[Fact]
public void WouldExceedWipLimitIfAdded_ShouldReturnFalse_WhenNoLimit()
{
    // Test unlimited columns
}

[Fact]
public void WouldExceedWipLimitIfAdded_ShouldReturnFalse_WhenBelowLimit()
{
    // Test normal case
}
```

**Label Entity - Validation:**
```csharp
[Fact]
public void Constructor_ShouldThrowOnInvalidColorHex()
{
    // Test color validation
}

[Fact]
public void Constructor_ShouldAcceptValidColorHex()
{
    // Test valid colors
}

[Fact]
public void Update_ShouldChangeNameAndColor()
{
    // Test update method
}
```

**Entity Base Class:**
```csharp
[Fact]
public void Entity_ShouldGenerateUniqueIds()
{
    // Test ID generation
}

[Fact]
public void Entity_ShouldSetCreatedAtOnConstruction()
{
    // Test timestamp initialization
}

[Fact]
public void Touch_ShouldUpdateUpdatedAt()
{
    // Test Touch() method
}

[Fact]
public void Equals_ShouldReturnTrueForSameId()
{
    // Test equality by ID
}
```

**Estimated New Tests:** 20+ additional domain tests
**Target Total:** 60+ domain tests

---

### Application Layer Tests (`Taskdeck.Application.Tests`)

**Status:** ❌ No tests yet - HIGH PRIORITY

#### Service Tests to Implement

**BoardService Tests:**
```csharp
public class BoardServiceTests
{
    private readonly Mock<IUnitOfWork> _unitOfWork;
    private readonly BoardService _service;

    public BoardServiceTests()
    {
        _unitOfWork = new Mock<IUnitOfWork>();
        _service = new BoardService(_unitOfWork.Object);
    }

    [Fact]
    public async Task CreateBoardAsync_ShouldReturnSuccess_WithValidData()
    {
        // Arrange
        var dto = new CreateBoardDto("Test Board", "Description");

        // Act
        var result = await _service.CreateBoardAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Test Board");
        _unitOfWork.Verify(u => u.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task CreateBoardAsync_ShouldReturnFailure_WhenNameIsEmpty()
    {
        // Test validation error handling
    }

    [Fact]
    public async Task GetBoardByIdAsync_ShouldReturnBoard_WhenExists()
    {
        // Test retrieval
    }

    [Fact]
    public async Task GetBoardByIdAsync_ShouldReturnNotFound_WhenDoesNotExist()
    {
        // Test not found scenario
    }

    [Fact]
    public async Task UpdateBoardAsync_ShouldUpdateFields()
    {
        // Test update logic
    }

    [Fact]
    public async Task ListBoardsAsync_ShouldReturnFilteredBoards()
    {
        // Test search and filter
    }

    [Fact]
    public async Task ArchiveBoardAsync_ShouldSetIsArchived()
    {
        // Test archival
    }
}
```

**CardService Tests (Critical):**
```csharp
public class CardServiceTests
{
    [Fact]
    public async Task CreateCardAsync_ShouldEnforceWipLimit()
    {
        // Arrange: Column at WIP limit
        // Act: Try to create card
        // Assert: Should return WipLimitExceeded error
    }

    [Fact]
    public async Task CreateCardAsync_ShouldAddLabels_WhenProvided()
    {
        // Test label assignment on creation
    }

    [Fact]
    public async Task UpdateCardAsync_ShouldReplaceLabels()
    {
        // Test label update logic
    }

    [Fact]
    public async Task UpdateCardAsync_ShouldBlockCard_WithReason()
    {
        // Test blocking logic
    }

    [Fact]
    public async Task MoveCardAsync_ShouldEnforceWipLimit_OnTargetColumn()
    {
        // CRITICAL: Test WIP limit when moving between columns
    }

    [Fact]
    public async Task MoveCardAsync_ShouldReorderCards_InTargetColumn()
    {
        // Test position reordering logic
    }

    [Fact]
    public async Task MoveCardAsync_ShouldAllowMove_WithinSameColumn()
    {
        // Test repositioning within column
    }

    [Fact]
    public async Task SearchCardsAsync_ShouldFilterByText()
    {
        // Test text search
    }

    [Fact]
    public async Task SearchCardsAsync_ShouldFilterByLabel()
    {
        // Test label filtering
    }

    [Fact]
    public async Task SearchCardsAsync_ShouldFilterByColumn()
    {
        // Test column filtering
    }
}
```

**ColumnService Tests:**
```csharp
public class ColumnServiceTests
{
    [Fact]
    public async Task CreateColumnAsync_ShouldAssignPosition()
    {
        // Test position assignment
    }

    [Fact]
    public async Task UpdateColumnAsync_ShouldUpdateWipLimit()
    {
        // Test WIP limit updates
    }

    [Fact]
    public async Task DeleteColumnAsync_ShouldFail_WhenCardsExist()
    {
        // Test deletion validation
    }
}
```

**LabelService Tests:**
```csharp
public class LabelServiceTests
{
    [Fact]
    public async Task CreateLabelAsync_ShouldValidateColorHex()
    {
        // Test color validation
    }

    [Fact]
    public async Task DeleteLabelAsync_ShouldRemoveFromCards()
    {
        // Test cascade label removal
    }
}
```

**Test Utilities:**
```csharp
public class TestDataBuilder
{
    public static Board CreateTestBoard(string name = "Test Board") { }
    public static Column CreateTestColumn(Guid boardId, int position = 0, int? wipLimit = null) { }
    public static Card CreateTestCard(Guid boardId, Guid columnId, string title = "Test Card") { }
    public static Label CreateTestLabel(Guid boardId, string name = "Test Label") { }
}

public class MockRepositoryBuilder
{
    public static Mock<IBoardRepository> CreateBoardRepository() { }
    public static Mock<IUnitOfWork> CreateUnitOfWork() { }
}
```

**Estimated Tests:** 50+ application tests
**Priority:** HIGH - Implement in next session

---

### API Layer Tests (`Taskdeck.Api.Tests`)

**Status:** ❌ Not created - MEDIUM PRIORITY

#### Integration Tests with WebApplicationFactory

```csharp
public class BoardsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public BoardsControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GET_Boards_ReturnsSuccessAndBoards()
    {
        // Act
        var response = await _client.GetAsync("/api/boards");

        // Assert
        response.EnsureSuccessStatusCode();
        var boards = await response.Content.ReadFromJsonAsync<List<BoardDto>>();
        boards.Should().NotBeNull();
    }

    [Fact]
    public async Task POST_Board_CreatesBoard()
    {
        // Arrange
        var newBoard = new CreateBoardDto("New Board", "Description");

        // Act
        var response = await _client.PostAsJsonAsync("/api/boards", newBoard);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task POST_Card_EnforcesWipLimit()
    {
        // Arrange: Create board and column at WIP limit
        // Act: Try to add card beyond limit
        // Assert: Should return 400 with WipLimitExceeded error
    }
}
```

**Test Categories:**
- ✅ CRUD operations for all resources
- ✅ Error responses (400, 404, 409, 500)
- ✅ Validation errors
- ✅ Business rule violations (WIP limits)
- ✅ Filtering and search
- ✅ Nested resource routes

**Estimated Tests:** 40+ integration tests
**Priority:** MEDIUM

---

## Frontend Testing

### Unit Tests (Vitest)

**Status:** ❌ Not set up - HIGH PRIORITY

#### Setup Required

```bash
cd frontend/taskdeck-web
npm install -D vitest @vue/test-utils jsdom @vitest/ui
```

**vitest.config.ts:**
```typescript
import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  test: {
    globals: true,
    environment: 'jsdom',
  },
})
```

#### Component Tests

**BoardsListView.test.ts:**
```typescript
import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import BoardsListView from '@/views/BoardsListView.vue'

describe('BoardsListView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('renders board list', () => {
    // Test component rendering
  })

  it('displays empty state when no boards', () => {
    // Test empty state
  })

  it('filters boards by search text', () => {
    // Test search functionality
  })

  it('navigates to board on click', () => {
    // Test navigation
  })
})
```

**BoardView.test.ts:**
```typescript
describe('BoardView', () => {
  it('displays columns in order', () => {
    // Test column ordering
  })

  it('shows WIP limit indicator', () => {
    // Test WIP display
  })

  it('groups cards by column', () => {
    // Test card grouping
  })

  it('highlights column when at WIP limit', () => {
    // Test WIP visual feedback
  })
})
```

**CardItem.test.ts:**
```typescript
describe('CardItem', () => {
  it('displays card title', () => {
    // Test basic rendering
  })

  it('shows labels with correct colors', () => {
    // Test label display
  })

  it('shows blocked indicator when blocked', () => {
    // Test blocked state
  })

  it('shows due date warning when overdue', () => {
    // Test due date logic
  })

  it('emits click event', () => {
    // Test interaction
  })
})
```

**BoardColumn.test.ts:**
```typescript
describe('BoardColumn', () => {
  it('displays column name', () => {
    // Test header
  })

  it('shows WIP limit and count', () => {
    // Test WIP display
  })

  it('renders cards in position order', () => {
    // Test card ordering
  })

  it('shows add card button', () => {
    // Test UI elements
  })
})
```

#### Store Tests

**boardStore.test.ts:**
```typescript
import { setActivePinia, createPinia } from 'pinia'
import { useBoardStore } from '@/store/boardStore'
import { beforeEach, describe, it, expect, vi } from 'vitest'

describe('Board Store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('fetches boards', async () => {
    // Mock API
    // Test fetchBoards action
  })

  it('creates board', async () => {
    // Test createBoard action
  })

  it('cardsByColumn groups cards correctly', () => {
    // Test computed property
  })

  it('sets error state on API failure', async () => {
    // Test error handling
  })
})
```

#### API Client Tests

**boardsApi.test.ts:**
```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { boardsApi } from '@/api/boardsApi'
import axios from 'axios'

vi.mock('axios')

describe('Boards API', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('getBoards calls correct endpoint', async () => {
    // Test API call
  })

  it('createBoard sends correct payload', async () => {
    // Test request format
  })

  it('handles errors gracefully', async () => {
    // Test error handling
  })
})
```

**Estimated Tests:** 60+ frontend unit tests
**Priority:** HIGH

---

### E2E Tests (Playwright)

**Status:** ❌ Not set up - MEDIUM PRIORITY

#### Setup

```bash
npm install -D @playwright/test
npx playwright install
```

#### Critical User Journeys

**01-create-board-flow.spec.ts:**
```typescript
import { test, expect } from '@playwright/test'

test('complete board creation flow', async ({ page }) => {
  // Navigate to app
  await page.goto('http://localhost:5173')

  // Create board
  await page.click('button:has-text("New Board")')
  await page.fill('input[name="boardName"]', 'My Test Board')
  await page.fill('textarea[name="description"]', 'Test description')
  await page.click('button:has-text("Create")')

  // Verify board appears
  await expect(page.locator('text=My Test Board')).toBeVisible()

  // Navigate to board
  await page.click('text=My Test Board')

  // Verify on board page
  await expect(page).toHaveURL(/\/boards\/[a-f0-9-]+/)
})
```

**02-kanban-workflow.spec.ts:**
```typescript
test('kanban workflow: create columns and cards', async ({ page }) => {
  // Setup: Create board first

  // Create columns
  await createColumn(page, 'To Do')
  await createColumn(page, 'In Progress', { wipLimit: 2 })
  await createColumn(page, 'Done')

  // Create cards
  await createCard(page, 'To Do', 'Task 1')
  await createCard(page, 'To Do', 'Task 2')

  // Verify cards appear
  await expect(page.locator('.card:has-text("Task 1")')).toBeVisible()

  // TODO: Test drag and drop when implemented
})
```

**03-wip-limit-enforcement.spec.ts:**
```typescript
test('WIP limit prevents card creation', async ({ page }) => {
  // Setup: Create column with WIP limit 1
  // Add 1 card to column

  // Try to add second card
  await page.click('button:has-text("Add Card")')
  await page.fill('input[name="title"]', 'Second Card')
  await page.click('button:has-text("Create")')

  // Expect error message
  await expect(page.locator('text=/WIP limit.*exceeded/i')).toBeVisible()
})
```

**04-card-management.spec.ts:**
```typescript
test('card CRUD operations', async ({ page }) => {
  // Create card
  // Edit card (when modal is implemented)
  // Add labels
  // Set due date
  // Block card
  // Delete card
})
```

**05-search-and-filter.spec.ts:**
```typescript
test('search and filter cards', async ({ page }) => {
  // Setup: Create cards with different labels

  // Test text search
  await page.fill('input[name="search"]', 'specific')
  await expect(page.locator('.card')).toHaveCount(1)

  // Test label filter
  await page.click('select[name="labelFilter"]')
  await page.click('option:has-text("Bug")')
  await expect(page.locator('.card')).toHaveCount(2)
})
```

**Estimated Tests:** 10-15 E2E tests
**Priority:** MEDIUM

---

## Integration Testing

### Full Stack Integration Tests

**Prerequisites:**
- Backend API running
- Frontend dev server running
- Clean test database

**Test Scenarios:**

1. **Board Lifecycle**
   - Create board via API
   - Verify appears in frontend
   - Update board
   - Archive board

2. **Column Management**
   - Create multiple columns
   - Set WIP limits
   - Reorder columns

3. **Card Operations**
   - Create cards in different columns
   - Move cards between columns
   - Verify WIP limit enforcement
   - Add/remove labels
   - Block/unblock cards

4. **Data Consistency**
   - Verify positions are sequential
   - Verify timestamps update correctly
   - Verify relationships maintained

---

## Test Data Strategy

### Test Data Builders

**Backend (C#):**
```csharp
public static class TestData
{
    public static Board CreateBoard(string name = "Test Board")
    {
        return new Board(name, "Test description");
    }

    public static Column CreateColumn(Guid boardId, string name = "Test Column", int? wipLimit = null)
    {
        return new Column(boardId, name, 0, wipLimit);
    }

    public static Card CreateCard(Guid boardId, Guid columnId, string title = "Test Card")
    {
        return new Card(boardId, columnId, title);
    }
}
```

**Frontend (TypeScript):**
```typescript
export const testData = {
  board: (overrides = {}): Board => ({
    id: crypto.randomUUID(),
    name: 'Test Board',
    description: 'Test description',
    isArchived: false,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  }),

  column: (boardId: string, overrides = {}): Column => ({
    id: crypto.randomUUID(),
    boardId,
    name: 'Test Column',
    position: 0,
    wipLimit: null,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  }),

  card: (boardId: string, columnId: string, overrides = {}): Card => ({
    id: crypto.randomUUID(),
    boardId,
    columnId,
    title: 'Test Card',
    description: '',
    dueDate: null,
    isBlocked: false,
    blockReason: null,
    position: 0,
    labels: [],
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  }),
}
```

### Database Seeding

**For Integration Tests:**
```csharp
public static class DatabaseSeeder
{
    public static async Task SeedTestData(TaskdeckDbContext context)
    {
        var board = TestData.CreateBoard("Seeded Board");
        context.Boards.Add(board);

        var column = TestData.CreateColumn(board.Id, "To Do");
        context.Columns.Add(column);

        var card = TestData.CreateCard(board.Id, column.Id, "Seeded Card");
        context.Cards.Add(card);

        await context.SaveChangesAsync();
    }
}
```

---

## CI/CD Integration

### GitHub Actions Workflow (Future)

```yaml
name: Test Suite

on: [push, pull_request]

jobs:
  backend-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      - name: Restore dependencies
        run: dotnet restore
        working-directory: ./backend
      - name: Build
        run: dotnet build --no-restore
        working-directory: ./backend
      - name: Test
        run: dotnet test --no-build --verbosity normal
        working-directory: ./backend

  frontend-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup Node.js
        uses: actions/setup-node@v3
        with:
          node-version: '20'
      - name: Install dependencies
        run: npm ci
        working-directory: ./frontend/taskdeck-web
      - name: Run tests
        run: npm test
        working-directory: ./frontend/taskdeck-web

  e2e-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup Node.js
        uses: actions/setup-node@v3
      - name: Install dependencies
        run: npm ci
        working-directory: ./frontend/taskdeck-web
      - name: Install Playwright
        run: npx playwright install --with-deps
      - name: Run E2E tests
        run: npm run test:e2e
        working-directory: ./frontend/taskdeck-web
```

---

## Test Priorities

### Priority 1: CRITICAL (Implement Next Session)

1. **Application Layer Tests**
   - CardService WIP limit tests
   - CardService move tests
   - All service CRUD operations

2. **Domain Layer - Additional Coverage**
   - Card label management tests
   - Position/movement tests
   - Column WIP limit edge cases

3. **Frontend - Basic Component Tests**
   - BoardView test
   - CardItem test
   - BoardStore test

### Priority 2: HIGH (Implement Soon)

1. **Frontend - Comprehensive Component Tests**
   - All component tests
   - All store tests
   - API client tests

2. **API Integration Tests**
   - CRUD operations
   - Error responses
   - WIP limit enforcement

### Priority 3: MEDIUM (Future Enhancement)

1. **E2E Tests**
   - Critical user journeys
   - Happy path flows

2. **Performance Tests**
   - Large dataset handling
   - Query performance

3. **Security Tests**
   - Input validation
   - SQL injection prevention

---

## Test Execution

### Commands

**Backend:**
```bash
cd backend
dotnet test                                          # All tests
dotnet test --filter "FullyQualifiedName~CardService"  # Specific tests
dotnet test /p:CollectCoverage=true                  # With coverage
```

**Frontend (when set up):**
```bash
cd frontend/taskdeck-web
npm test                  # Run all tests
npm test -- --coverage   # With coverage
npm test -- --watch      # Watch mode
npm test -- CardItem     # Specific test
```

**E2E (when set up):**
```bash
cd frontend/taskdeck-web
npm run test:e2e          # All E2E tests
npm run test:e2e -- --headed  # With browser visible
```

---

## Success Criteria

### Phase 1 (Next Session)
- [ ] 60+ domain tests passing
- [ ] 50+ application tests passing
- [ ] 0 compilation errors
- [ ] Coverage >85% in critical paths

### Phase 2
- [ ] 60+ frontend unit tests passing
- [ ] Frontend coverage >70%
- [ ] All stores tested

### Phase 3
- [ ] 40+ API integration tests passing
- [ ] 10+ E2E tests for critical flows
- [ ] CI/CD pipeline running all tests

---

## Notes

- **Mock vs Real Database:** Use mocks for unit tests, real in-memory DB for integration tests
- **Test Isolation:** Each test should set up and tear down its own data
- **Flaky Tests:** If a test is flaky, fix it immediately or mark it as quarantined
- **Test Naming:** Use `MethodName_Scenario_ExpectedBehavior` convention
- **Arrange-Act-Assert:** Follow AAA pattern consistently

---

**END OF TEST SUITE PLAN**

*This plan should be updated as tests are implemented and new testing needs are discovered.*
