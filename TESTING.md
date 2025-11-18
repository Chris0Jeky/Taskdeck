# Testing Guide

This document provides comprehensive testing guidelines for the Taskdeck project.

## Table of Contents

- [Backend Testing](#backend-testing)
- [Frontend Testing](#frontend-testing)
- [Running Tests](#running-tests)
- [Writing Tests](#writing-tests)
- [Test Coverage](#test-coverage)

## Backend Testing

### Framework and Tools

The backend uses the following testing stack:

- **xUnit** - Test framework
- **FluentAssertions** - Fluent assertion library
- **In-memory SQLite** - Database for integration tests
- **TestDataBuilder** - Helper class for creating test entities

### Test Structure

Backend tests are organized into two projects:

1. **Taskdeck.Domain.Tests** - Domain entity and business logic tests
2. **Taskdeck.Application.Tests** - Service layer and integration tests

### Running Backend Tests

```bash
cd backend

# Run all tests
dotnet test

# Run tests with coverage
dotnet test /p:CollectCoverage=true

# Run specific test class
dotnet test --filter "FullyQualifiedName~CardServiceTests"

# Run tests in watch mode
dotnet watch test
```

### Writing Backend Tests

#### Using TestDataBuilder

Always use `TestDataBuilder` to create test entities instead of reflection:

```csharp
// Create a column with cards
var card1 = TestDataBuilder.CreateCard(boardId, columnId, "Card 1");
var card2 = TestDataBuilder.CreateCard(boardId, columnId, "Card 2");
var column = TestDataBuilder.CreateColumnWithCards(
    boardId,
    "To Do",
    new[] { card1, card2 }
);

// Create a board with columns
var board = TestDataBuilder.CreateBoardWithColumns(
    "My Board",
    new[] { column1, column2 }
);

// Create a card with labels
var cardLabel1 = TestDataBuilder.CreateCardLabelWithLabel(cardId, label1);
var cardLabel2 = TestDataBuilder.CreateCardLabelWithLabel(cardId, label2);
var card = TestDataBuilder.CreateCardWithLabels(
    boardId,
    columnId,
    "Card Title",
    new[] { cardLabel1, cardLabel2 }
);
```

#### Service Tests Pattern

Service tests follow the Arrange-Act-Assert pattern:

```csharp
[Fact]
public async Task UpdateCard_WithValidData_ShouldUpdateCard()
{
    // Arrange
    var board = TestDataBuilder.CreateBoard("Test Board");
    var column = TestDataBuilder.CreateColumn(board.Id, "To Do");
    var card = TestDataBuilder.CreateCard(board.Id, column.Id, "Original Title");

    await _context.AddRangeAsync(board, column, card);
    await _context.SaveChangesAsync();

    var updateDto = new UpdateCardDto(
        Title: "Updated Title",
        Description: "Updated description",
        DueDate: null,
        IsBlocked: null,
        BlockReason: null,
        LabelIds: null
    );

    // Act
    var result = await _cardService.UpdateCardAsync(board.Id, card.Id, updateDto);

    // Assert
    result.IsSuccess.Should().BeTrue();
    result.Value.Title.Should().Be("Updated Title");
    result.Value.Description.Should().Be("Updated description");
}
```

#### Testing Error Cases

Always test both success and failure paths:

```csharp
[Fact]
public async Task DeleteColumn_WithCards_ShouldFail()
{
    // Arrange
    var board = TestDataBuilder.CreateBoard("Test Board");
    var card = TestDataBuilder.CreateCard(board.Id, columnId, "Card");
    var column = TestDataBuilder.CreateColumnWithCards(
        board.Id,
        "To Do",
        new[] { card }
    );

    await _context.AddRangeAsync(board, column, card);
    await _context.SaveChangesAsync();

    // Act
    var result = await _columnService.DeleteColumnAsync(board.Id, column.Id);

    // Assert
    result.IsSuccess.Should().BeFalse();
    result.ErrorCode.Should().Be(ErrorCodes.Conflict);
}
```

### Internal Members Access

Domain entities use internal setters for navigation properties. Tests can access these through `InternalsVisibleTo`:

```xml
<!-- In Taskdeck.Domain.csproj -->
<ItemGroup>
  <InternalsVisibleTo Include="Taskdeck.Application.Tests" />
</ItemGroup>
```

## Frontend Testing

### Framework and Tools

The frontend uses the following testing stack:

- **Vitest** - Fast unit test framework
- **@vue/test-utils** - Vue component testing utilities
- **happy-dom** - Lightweight DOM implementation
- **Pinia** - State management testing

### Test Structure

Frontend tests are located in `frontend/taskdeck-web/src/tests/`:

- `store/` - Pinia store tests
- `components/` - Vue component tests
- `setup.ts` - Global test configuration

### Running Frontend Tests

```bash
cd frontend/taskdeck-web

# Install dependencies (first time only)
npm install

# Run tests in watch mode
npm run test

# Run tests once
npm run test -- --run

# Run tests with UI
npm run test:ui

# Run tests with coverage
npm run test:coverage

# Run specific test file
npm run test -- boardStore.spec.ts
```

### Writing Frontend Tests

#### Store Tests

Store tests mock API calls and verify state updates:

```typescript
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useBoardStore } from '../../store/boardStore'
import * as boardsApi from '../../api/boardsApi'

vi.mock('../../api/boardsApi')

describe('boardStore', () => {
  let store: ReturnType<typeof useBoardStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    store = useBoardStore()
    vi.clearAllMocks()
  })

  it('should create a board', async () => {
    const newBoard = { id: '1', name: 'New Board', ... }
    vi.mocked(boardsApi.createBoard).mockResolvedValue(newBoard)

    const result = await store.createBoard({ name: 'New Board' })

    expect(result).toEqual(newBoard)
    expect(store.boards).toContainEqual(newBoard)
  })
})
```

#### Component Tests

Component tests mount Vue components and test user interactions:

```typescript
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import CardModal from '../../components/board/CardModal.vue'
import { useBoardStore } from '../../store/boardStore'

vi.mock('../../store/boardStore', () => ({
  useBoardStore: vi.fn(),
}))

describe('CardModal', () => {
  let mockStore: any

  beforeEach(() => {
    setActivePinia(createPinia())
    mockStore = {
      updateCard: vi.fn().mockResolvedValue({}),
      deleteCard: vi.fn().mockResolvedValue(undefined),
    }
    vi.mocked(useBoardStore).mockReturnValue(mockStore)
  })

  it('should render when open', () => {
    const wrapper = mount(CardModal, {
      props: {
        card: { id: '1', title: 'Test', ... },
        isOpen: true,
        labels: [],
      },
    })

    expect(wrapper.find('h2').text()).toBe('Edit Card')
  })

  it('should call updateCard on save', async () => {
    const wrapper = mount(CardModal, { props: { ... } })

    const titleInput = wrapper.find('#card-title')
    await titleInput.setValue('Updated Title')

    const saveButton = wrapper
      .findAll('button')
      .find((btn) => btn.text().includes('Save Changes'))
    await saveButton?.trigger('click')

    expect(mockStore.updateCard).toHaveBeenCalled()
  })
})
```

#### Testing User Interactions

Test user interactions with Vue Test Utils:

```typescript
// Text input
const input = wrapper.find('#field-name')
await input.setValue('New Value')

// Checkbox
const checkbox = wrapper.find('#checkbox-id')
await checkbox.setValue(true)

// Button click
const button = wrapper.find('button')
await button.trigger('click')

// Find button by text
const saveButton = wrapper
  .findAll('button')
  .find((btn) => btn.text().includes('Save'))
await saveButton?.trigger('click')

// Check emitted events
expect(wrapper.emitted('close')).toBeTruthy()
expect(wrapper.emitted('updated')).toBeTruthy()
```

#### Mocking Browser APIs

Mock window methods for confirmation dialogs:

```typescript
it('should confirm before deleting', async () => {
  const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)

  // ... trigger delete

  expect(confirmSpy).toHaveBeenCalled()
  confirmSpy.mockRestore()
})
```

### Test Configuration

The `vitest.config.ts` file configures the test environment:

```typescript
export default defineConfig({
  plugins: [vue()],
  test: {
    globals: true,
    environment: 'happy-dom',
    setupFiles: './src/tests/setup.ts',
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json', 'html'],
    },
  },
})
```

The `setup.ts` file creates a fresh Pinia instance for each test:

```typescript
import { beforeAll, afterEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

beforeAll(() => {
  setActivePinia(createPinia())
})

afterEach(() => {
  setActivePinia(createPinia())
})
```

## Test Coverage

### Current Coverage Status

**Backend:**
- Total Tests: 124
- Pass Rate: 100%
- Domain Tests: Full coverage of entity business logic
- Service Tests: Full coverage of CRUD operations

**Frontend:**
- Store Tests: Full coverage of all Pinia actions
- Component Tests: Full coverage of all modal components
  - CardModal
  - BoardSettingsModal
  - ColumnEditModal
  - LabelManagerModal

### Coverage Goals

We aim for:
- **Backend**: 80%+ line coverage, 100% coverage of business logic
- **Frontend**: 70%+ line coverage, 100% coverage of critical user flows

### Viewing Coverage Reports

**Backend:**
```bash
cd backend
dotnet test /p:CollectCoverage=true
# Coverage report is generated in each test project directory
```

**Frontend:**
```bash
cd frontend/taskdeck-web
npm run test:coverage
# Open coverage/index.html in your browser
```

## Best Practices

### General

1. **Test behavior, not implementation** - Focus on what the code does, not how it does it
2. **One assertion per test** - Keep tests focused and easy to debug
3. **Use descriptive test names** - Test names should describe the scenario and expected outcome
4. **Arrange-Act-Assert** - Structure tests clearly with setup, action, and verification
5. **Test edge cases** - Don't just test the happy path

### Backend-Specific

1. **Use TestDataBuilder** - Never use reflection to set up test data
2. **Clean database between tests** - Each test should be independent
3. **Test the Result pattern** - Always check both `IsSuccess` and error codes
4. **Mock repositories** - Use in-memory database for integration tests
5. **Test domain validation** - Ensure business rules are enforced

### Frontend-Specific

1. **Mock external dependencies** - Mock API calls, stores, and routers
2. **Test user workflows** - Simulate real user interactions
3. **Check accessibility** - Verify form validation and disabled states
4. **Test error handling** - Verify error messages and recovery flows
5. **Keep tests fast** - Use happy-dom instead of jsdom for speed

## Continuous Integration

Tests run automatically on every commit. Ensure all tests pass before:
- Creating a pull request
- Merging to main
- Deploying to production

## Troubleshooting

### Common Issues

**Backend:**

- **Tests fail with "Object reference not set"** - Use TestDataBuilder instead of reflection
- **Database locked errors** - Ensure each test uses a unique database context
- **Tests pass locally but fail in CI** - Check for timing issues or environment dependencies

**Frontend:**

- **"Cannot find module" errors** - Check import paths and vitest.config.ts aliases
- **"store is not defined"** - Ensure `setActivePinia(createPinia())` is called in beforeEach
- **Component not rendering** - Check that `isOpen` prop is true for modal components
- **Mock not working** - Ensure `vi.clearAllMocks()` is called in beforeEach

### Getting Help

If you encounter issues:
1. Check this documentation
2. Review existing tests for examples
3. Check the [Vitest documentation](https://vitest.dev/)
4. Check the [Vue Test Utils documentation](https://test-utils.vuejs.org/)
5. Check the [xUnit documentation](https://xunit.net/)

## Additional Resources

- [Vitest Documentation](https://vitest.dev/)
- [Vue Test Utils](https://test-utils.vuejs.org/)
- [xUnit Documentation](https://xunit.net/)
- [FluentAssertions Documentation](https://fluentassertions.com/)
- [Testing Best Practices](https://kentcdodds.com/blog/common-mistakes-with-react-testing-library)
