# Testing Strategy

This project uses a **two-tier testing approach** to balance speed and confidence:

## Tier 1: Fast Integration Tests ?

**File:** `NoteKeeperControllerIntegrationTests.cs`

**Purpose:** Fast, isolated tests for rapid feedback during development

**Characteristics:**
- ? Uses `WebApplicationFactory` with in-memory database
- ? Runs in **parallel** (each test isolated)
- ? **Fast** (~5-10 seconds for all tests)
- ? No external dependencies (Azure, SQL Server)
- ? Run on every build/commit
- ? Ideal for TDD and CI/CD pipelines

**Run with:**
```bash
# Run only integration tests (default - runs all tests except E2E)
dotnet test --filter "FullyQualifiedName!~E2E"

# Or just run all tests (integration runs fast)
dotnet test
```

**When to use:**
- During active development
- Before committing code
- In CI/CD pipelines
- When you need quick feedback

---

## Tier 2: E2E (End-to-End) Tests ??

**File:** `NoteKeeperControllerE2ETests.cs`

**Purpose:** Production validation against real Azure environment

**Characteristics:**
- ? Tests against **real Azure SQL Database**
- ? Validates network, deployment, and Azure services
- ? Runs **sequentially** (shared database)
- ?? Slower (~45-60 seconds)
- ?? Requires Azure to be running
- ?? Requires internet connection

**Run with:**
```bash
# Run only E2E tests
dotnet test --filter "Category=E2E"

# Or by name
dotnet test --filter "FullyQualifiedName~E2ETests"
```

**When to use:**
- Before deployment to production
- As part of nightly builds
- When validating production behavior
- When testing Azure-specific features

---

## Test Comparison

| Feature | Integration Tests | E2E Tests |
|---------|------------------|-----------|
| **Speed** | Fast (5-10s) | Slow (45-60s) |
| **Database** | In-Memory | Azure SQL |
| **Isolation** | ? Yes | ? Shared DB |
| **Parallel** | ? Yes | ? Sequential |
| **Dependencies** | None | Azure required |
| **Run Frequency** | Every commit | Pre-deployment |
| **CI/CD** | ? Always | Optional |

---

## Architecture

### Integration Tests Architecture

```
Test ? WebApplicationFactory ? In-Memory DB
                             ? Mock Services (if needed)
                             ? Real Controllers
                             ? Real Business Logic
```

**CustomWebApplicationFactory:**
- Replaces SQL Server with in-memory database
- Each test fixture gets a fresh database
- No seeding (tests control their own data)
- Supports parallel execution

### E2E Tests Architecture

```
Test ? HttpClient ? Azure App Service ? Controllers ? Azure SQL
                                                   ? Azure OpenAI
                                                   ? Other Azure Services
```

**Sequential Execution:**
- Uses `[Collection("Sequential")]` attribute
- Prevents database conflicts
- Handles existing data gracefully

---

## Best Practices

### Integration Tests
```csharp
// Each test is independent
[Fact]
public async Task CreateNote_WithValidData_ReturnsCreated()
{
    // Fresh database for this test
    var request = new CreateNoteRequest { ... };
    var response = await _client.PostAsJsonAsync("/NoteKeeper", request);
    
    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Created);
}
```

### E2E Tests
```csharp
// Handles existing data
[Fact]
public async Task CreateNote_WhenMaxNotesLimitReached_ReturnsForbidden()
{
    // Check current database state
    var existingNotes = await _client.GetAsync("/NoteKeeper");
    
    // Adjust to reach exactly 10 notes
    // Then test the limit
}
```

---

## Recommended Workflow

### During Development (Daily)
```bash
# Run fast integration tests frequently
dotnet test --filter "FullyQualifiedName!~E2E"

# Or use watch mode
dotnet watch test --filter "FullyQualifiedName!~E2E"
```

### Before Committing
```bash
# Run all tests including E2E
dotnet test
```

### CI/CD Pipeline
```yaml
# Fast feedback - run on every PR
- name: Run Integration Tests
  run: dotnet test --filter "FullyQualifiedName!~E2E"

# Slower validation - run before merge to main
- name: Run E2E Tests
  run: dotnet test --filter "Category=E2E"
  if: github.ref == 'refs/heads/main'
```

---

## Migration Guide

If you want to convert your existing project to this pattern:

1. **Keep your existing tests** - rename to `*E2ETests.cs`
2. **Create `CustomWebApplicationFactory`** for in-memory testing
3. **Copy tests** to new `*IntegrationTests.cs` file
4. **Update E2E tests** to handle existing data
5. **Add `[Trait("Category", "E2E")]`** to E2E test class
6. **Run integration tests** as your default
7. **Run E2E tests** before deployment

---

## Troubleshooting

### Integration Tests Failing
- Check that `Microsoft.EntityFrameworkCore.InMemory` package is installed
- Verify `CustomWebApplicationFactory` is properly configured
- Ensure tests don't depend on external services

### E2E Tests Failing
- Verify Azure connection is working
- Check that MaxNotes limit isn't already reached
- Ensure tests run sequentially (`[Collection("Sequential")]`)
- Verify the Azure app is deployed and running

---

## Summary

? **Use Integration Tests** for fast feedback during development  
? **Use E2E Tests** for production validation before deployment  
? **Run both** to ensure quality and confidence

For questions or issues, refer to the `.github/copilot-instructions.md` file.
