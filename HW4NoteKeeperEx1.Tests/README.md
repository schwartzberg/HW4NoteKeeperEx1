# HW4NoteKeeperEx1 Integration Tests

This test project contains comprehensive integration tests for the HW4NoteKeeperEx1 REST API.

## Test Framework and Libraries

- **xUnit**: Primary test framework
- **FluentAssertions**: For readable and expressive assertions
- **Microsoft.AspNetCore.Mvc.Testing**: For integration testing using WebApplicationFactory
- **Moq**: Available for mocking (if needed for future tests)

## Test Coverage

### GET /NoteKeeper (Get All Notes)
- ? Returns success with empty or populated list

### POST /NoteKeeper (Create Note)
**Positive Tests:**
- ? Creates note with valid data and returns 201 Created
- ? Validates CreatedDateUtc is set and ModifiedDateUtc is null
- ? Returns the same summary and details as provided
- ? Verifies Location header is set

**Negative Tests:**
- ? Null summary returns 400 Bad Request
- ? Empty summary returns 400 Bad Request
- ? Whitespace-only summary returns 400 Bad Request
- ? Null details returns 400 Bad Request
- ? Summary exceeding 60 characters returns 400 Bad Request

### GET /NoteKeeper/{noteId} (Get Note by ID)
**Positive Tests:**
- ? Returns note with valid ID ..

**Negative Tests:**
- ? Non-existent ID returns 404 Not Found
- ? Empty ID returns 400 Bad Request

### PATCH /NoteKeeper/{noteId} (Update Note)
**Positive Tests:**
- ? Updates summary successfully
- ? Updates details and regenerates tags
- ? Validates ModifiedDateUtc is set and after CreatedDateUtc

**Negative Tests:**
- ? Non-existent ID returns 404 Not Found
- ? Both fields null/empty returns 400 Bad Request
- ? Empty ID returns 405 Method Not Allowed

### DELETE /NoteKeeper/{noteId} (Delete Note)
**Positive Tests:**
- ? Deletes existing note and returns 204 No Content
- ? Verifies note is actually deleted (subsequent GET returns 404)

**Negative Tests:**
- ? Non-existent ID returns 404 Not Found
- ? Empty ID returns 405 Method Not Allowed

## Running the Tests

### In Visual Studio
1. Open Test Explorer (Test > Test Explorer)
2. Click "Run All Tests"

### From Command Line
```bash
cd HW4NoteKeeperEx1.Tests
dotnet test
```

### With Coverage
```bash
dotnet test /p:CollectCoverage=true
```

## Testing Against Azure Deployment

To test against your Azure App Service instead of the local instance, modify the `WebApplicationFactory` setup:

```csharp
public NoteKeeperControllerTests()
{
    _client = new HttpClient
    {
        BaseAddress = new Uri("https://app-notekeeper-cscie94-ps-HW4-1-gjegduaqfccbd2bt.swedencentral-01.azurewebsites.net/")
    };
}
```

## Notes

- All tests use integration testing via `WebApplicationFactory`, which starts the actual API
- Tests create, modify, and delete real data in the in-memory store
- Each test is independent and creates its own test data
- Date validation ensures CreatedDateUtc and ModifiedDateUtc are properly set and have correct relationships
