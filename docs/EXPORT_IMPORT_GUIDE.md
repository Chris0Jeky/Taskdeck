# Export/Import & Database Sharing Guide

**Status:** Scaffolding Complete - Implementation Pending
**Last Updated:** 2026-02-11

## Overview

Taskdeck supports exporting and importing board data to enable database sharing, backup/restore, and collaboration workflows. This guide covers the architecture and implementation plan.

## Use Cases

1. **Backup and Restore:** Export board to file, restore later if needed
2. **Database Sharing:** Export database file, share with collaborators
3. **Board Migration:** Move boards between Taskdeck instances
4. **Collaboration:** Export board, colleagues import and work on it
5. **Archival:** Long-term storage of completed projects

## Architecture

### Export Formats

**1. JSON Format (Recommended)**
- Human-readable and editable
- Portable across platforms
- Supports selective export (single board or full database)
- Includes metadata (export timestamp, version, exporter)

**2. SQLite Database File (Alternative)**
- Full-fidelity backup
- Includes all metadata and relationships
- Larger file size
- Best for complete backups

### Export/Import Service Interface

**Service:** `IExportImportService` (Implementation Pending)

**Operations:**
- Export single board to JSON
- Import board from JSON
- Export full database to file
- Import full database from file

### DTOs

**ExportBoardDto:**
```csharp
record ExportBoardDto(
    BoardDto Board,
    IEnumerable<ColumnDto> Columns,
    IEnumerable<CardDto> Cards,
    IEnumerable<LabelDto> Labels,
    IEnumerable<BoardAccessDto> Accesses,
    DateTimeOffset ExportedAt,
    string ExportedBy
);
```

**ImportBoardDto:**
```csharp
record ImportBoardDto(
    string Name,
    string? Description,
    IEnumerable<ImportColumnDto> Columns,
    IEnumerable<ImportCardDto> Cards,
    IEnumerable<ImportLabelDto> Labels
);
```

**ImportResultDto:**
```csharp
record ImportResultDto(
    bool Success,
    Guid? BoardId,
    string? ErrorMessage,
    int ColumnsImported,
    int CardsImported,
    int LabelsImported
);
```

## Export Format Specification

### JSON Structure

```json
{
  "version": "1.0",
  "exportedAt": "2026-02-11T08:30:00Z",
  "exportedBy": "john.doe",
  "board": {
    "name": "Project Alpha",
    "description": "Main project board",
    "isArchived": false
  },
  "columns": [
    {
      "name": "To Do",
      "position": 0,
      "wipLimit": 5
    },
    {
      "name": "In Progress",
      "position": 1,
      "wipLimit": 3
    }
  ],
  "cards": [
    {
      "title": "Implement feature X",
      "description": "Detailed description here",
      "columnName": "To Do",
      "position": 0,
      "dueDate": "2026-02-20T00:00:00Z",
      "labels": ["high-priority", "backend"],
      "isBlocked": false,
      "blockReason": null
    }
  ],
  "labels": [
    {
      "name": "high-priority",
      "color": "#FF0000"
    },
    {
      "name": "backend",
      "color": "#0000FF"
    }
  ],
  "accesses": [
    {
      "userName": "john.doe",
      "role": "Owner"
    },
    {
      "userName": "jane.smith",
      "role": "Editor"
    }
  ]
}
```

### Version Compatibility

- Export includes version number
- Import validates version compatibility
- Supports forward/backward compatible changes
- Rejects incompatible versions with clear error message

## Import Conflict Resolution

### Strategies

**1. Overwrite (Default)**
- Import creates new board with new ID
- Preserves existing boards unchanged
- Safest option

**2. Merge**
- Import into existing board
- Append columns/cards if names don't conflict
- Update if exact match found
- Requires user confirmation

**3. Skip Duplicates**
- Import only items that don't exist
- Based on name matching
- Reports skipped items

### User Mapping

When importing boards with permissions:
- Option 1: Map to existing users by username/email
- Option 2: Create new users if they don't exist
- Option 3: Grant all access to importer only

## CLI Commands (To Be Implemented)

### Export Commands

```bash
# Export single board to JSON
taskdeck export --board <board-id> --output board-backup.json

# Export board with specific format
taskdeck export --board <board-id> --format json --output board.json

# Export full database
taskdeck export --full --output taskdeck-backup.db
```

### Import Commands

```bash
# Import board from JSON
taskdeck import --file board.json --owner <user-id>

# Import with conflict resolution
taskdeck import --file board.json --strategy overwrite

# Import full database (replace existing)
taskdeck import --full --file taskdeck-backup.db --confirm
```

## API Endpoints (To Be Implemented)

### Export Endpoints

```
GET /api/export/boards/{boardId}
GET /api/export/boards/{boardId}/json
GET /api/export/database
```

### Import Endpoints

```
POST /api/import/boards
POST /api/import/boards/json
POST /api/import/database
```

## Implementation Checklist

### Phase 1: Board Export/Import
- [ ] Implement `ExportImportService`
- [ ] Add JSON serialization for all DTOs
- [ ] Create export API endpoint
- [ ] Create import API endpoint
- [ ] Add CLI `export` command
- [ ] Add CLI `import` command
- [ ] Write export/import integration tests

### Phase 2: Conflict Resolution
- [ ] Implement overwrite strategy
- [ ] Implement merge strategy
- [ ] Implement skip duplicates strategy
- [ ] Add user mapping logic
- [ ] Add conflict resolution UI
- [ ] Write conflict resolution tests

### Phase 3: Full Database Backup/Restore
- [ ] Implement database export
- [ ] Implement database import
- [ ] Add validation and integrity checks
- [ ] Add progress reporting for large exports
- [ ] Add CLI database backup command
- [ ] Write database backup tests

### Phase 4: Advanced Features
- [ ] Selective export (choose columns/cards)
- [ ] Incremental backup (only changes since last export)
- [ ] Compression for large exports
- [ ] Encryption for sensitive data
- [ ] Export to other formats (CSV, Markdown)

## Security Considerations

1. **Authorization:** Only board owners/admins can export with permissions
2. **Sensitive Data:** Option to exclude user emails/passwords from exports
3. **File Validation:** Validate import files before processing
4. **Size Limits:** Enforce maximum file size for imports
5. **Audit Trail:** Log all export/import operations

## Usage Examples (Future)

### Exporting a Board

```csharp
var result = await exportService.ExportBoardAsync(boardId, userId);
if (result.IsSuccess)
{
    var json = JsonSerializer.Serialize(result.Value);
    await File.WriteAllTextAsync("board-backup.json", json);
}
```

### Importing a Board

```csharp
var json = await File.ReadAllTextAsync("board-backup.json");
var result = await exportService.ImportBoardFromJsonAsync(json, userId);
if (result.IsSuccess)
{
    Console.WriteLine($"Imported board: {result.Value.BoardId}");
    Console.WriteLine($"  Columns: {result.Value.ColumnsImported}");
    Console.WriteLine($"  Cards: {result.Value.CardsImported}");
}
```

## Testing Strategy

1. **Unit Tests:** Test serialization, deserialization, mapping logic
2. **Integration Tests:** Test complete export/import workflows
3. **Edge Cases:** Empty boards, large boards, invalid data
4. **Version Tests:** Test import of older export formats

## File Format Versioning

**Version 1.0 (Current)**
- Basic board structure
- Columns, cards, labels
- User permissions
- Card relationships (labels)

**Future Versions**
- Version 1.1: Add card attachments, comments
- Version 1.2: Add recurring tasks, time tracking
- Version 2.0: Add board templates, automation rules

## Next Steps

1. Review export format specification
2. Implement `ExportImportService`
3. Create export/import endpoints
4. Add CLI commands
5. Write comprehensive tests
6. Add UI for export/import

## References

- Service Interface: `backend/src/Taskdeck.Application/Services/IExportImportService.cs`
- DTOs: `backend/src/Taskdeck.Application/DTOs/AuditAndExportDtos.cs`
- Use Cases: This document, sections above
