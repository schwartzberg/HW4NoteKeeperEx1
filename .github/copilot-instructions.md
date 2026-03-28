# GitHub Copilot Instructions for This Project

## User Account Information

- **GitHub username**: `schwartzberg`
- **Azure email**: `paulschwartzberg@outlook.com`
- **Azure subscription**: `2026 Assignments_Paul_Schwartzberg`
- **Azure subscription ID**: `1ba1c9cd-efc8-4259-a0a3-8a64e1ae9482`
- **Azure tenant ID**: `d607a394-7dc0-4c7d-8b9e-a9ed69c728b9`

## General Principles

### When Assisting with This Project

**1. Clarification First, Execution Second**
- Do not make assumptions about requirements, configurations, or desired outcomes
- Ask clarifying questions when the request is ambiguous or could be interpreted multiple ways
- Verify understanding before making changes, especially when:
  - The request involves multiple files or components
  - There are multiple valid implementation approaches
  - The scope or requirements are not explicitly stated

**2. Context Gathering Before Code Generation**
- Always read relevant files before suggesting changes
- Use `get_file`, `code_search`, and `get_projects_in_solution` to understand the codebase
- Check for existing patterns and conventions in the project
- Identify dependencies and relationships between components

**3. Pattern-Based, Not Project-Specific**
- Focus on reusable patterns that work across similar projects
- Document the "why" behind patterns, not just the "what"
- Avoid hardcoding project-specific values in examples (use placeholders like `[YourProjectName]`)
- When suggesting solutions, explain trade-offs and alternatives

**4. Learning from Iterations**
When encountering issues that require multiple attempts to resolve, identify:
- **Root Cause**: What fundamental understanding was missing?
- **Pattern to Document**: What reusable pattern would prevent this issue?
- **Prerequisites**: What conditions must be met for the pattern to work?

**Common Iteration Causes to Avoid**:
- ? Not checking if `InternalsVisibleTo` is configured correctly
- ? Assuming state isolation without verifying static field usage
- ? Not confirming `Program` class visibility before creating tests
- ? Generating code without reading existing project structure

### Maintaining These Instructions

**IMPORTANT: Documentation Maintenance Protocol**

**ALWAYS UPDATE MYPROMPTS.MD WITH EVERY USER PROMPT**

When the user asks questions or requests help:
1. **MANDATORY: Always update `HW4NoteKeeperEx1\MyPrompts.md`** with the user's prompt text
   - Add it as a new numbered section (continue from the last number)
   - Include the full prompt text in a code block
   - Add context about what issue was being solved
   - Add the resolution/solution applied
   - Maintain chronological order
   - This is REQUIRED for every user interaction, not optional
   - **File location**: `C:\Users\schwa\Documents\H_DCE\cloud_computing_openai_e_94\assignments\04-Assignment\HW4NoteKeeperEx1\HW4NoteKeeperEx1\MyPrompts.md`

2. **Update this file (`.github\copilot-instructions.md`)** when learnings occur:
   - Add reusable patterns discovered during problem-solving
   - Document solutions to common issues
   - Capture best practices specific to this project's tech stack

**When to Update This File**:
- ? New patterns emerge that are reusable across projects
- ? Common issues are discovered that have systematic solutions
- ? Team conventions change or evolve
- ? Framework versions change requiring different approaches
- ? User explicitly asks to update copilot instructions

**When NOT to Update This File**:
- ? Project-specific business logic
- ? Temporary workarounds or hotfixes
- ? Individual preferences that aren't team standards
- ? One-off solutions that don't represent patterns

**How to Propose Updates**:
1. Identify the pattern or principle
2. Explain the problem it solves
3. Provide a generic example with placeholders
4. Suggest where in this document it belongs
5. Let the user approve before modifying

## Project Overview
This is an ASP.NET Core 10.0 Web API project with a Movies REST API and comprehensive integration tests.

## Integration Testing Patterns

### Required Configuration for WebApplicationFactory Tests

When creating integration tests using `Microsoft.AspNetCore.Mvc.Testing`, follow these patterns:

#### 1. Main Project Configuration (`GitHubCopilot.csproj`)

**IMPORTANT**: Always expose internal types to test projects using `InternalsVisibleTo`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="MovieIntegrationTests" />
  <InternalsVisibleTo Include="[YourTestProjectName]" />
</ItemGroup>
```

**Note**: Use `ItemGroup` format, NOT `PropertyGroup` format for better reliability.

#### 2. Program.cs Pattern

The `Program` class MUST be `public partial` for WebApplicationFactory to work:

```csharp
namespace GitHubCopilot
{
    public partial class Program
    {
        public static void Main(string[] args)
        {
            // Application setup
        }
    }
}
```

### Custom WebApplicationFactory for State Management

#### Purpose
When controllers use static state (like `static ConcurrentDictionary`), create a custom `WebApplicationFactory` to reset state between tests.

#### Implementation Pattern

**1. Add Internal Clear Method to Controller:**

```csharp
public class MoviesController : ControllerBase
{
    private static readonly ConcurrentDictionary<Guid, Movie> _movies = new();

    // Internal method for testing purposes only
    internal static void ClearMovies()
    {
        _movies.Clear();
    }
}
```

**2. Create CustomWebApplicationFactory:**

```csharp
public class CustomWebApplicationFactory : WebApplicationFactory<YourNamespace.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Clear static state before each test run
            YourController.ClearStaticState();
        });

        base.ConfigureWebHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Clear state after disposal as well
            YourController.ClearStaticState();
        }
        base.Dispose(disposing);
    }
}
```

**3. Use in Test Class:**

```csharp
public class ControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    
    public ControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        
        // Ensure clean state before each test
        YourController.ClearStaticState();
    }
}
```

### Integration Test Structure Patterns

#### Standard Test Naming Convention
Use descriptive names following the pattern: `MethodName_ExpectedResult_Condition`

Examples:
- `CreateMovie_ReturnsCreatedMovie_WithValidData`
- `GetMovieById_ReturnsNotFound_WhenMovieDoesNotExist`
- `UpdateMovie_ReturnsBadRequest_WithInvalidGuid`

#### Test Organization
Use clear section comments for complex tests:

```csharp
[Fact]
public async Task FullCrudCycle_CreateReadUpdateDeleteMovie_WorksCorrectly()
{
    // ==================== CREATE ====================
    // Arrange
    var newItem = new Item { /* ... */ };
    
    // Act - Create
    var createResponse = await _client.PostAsJsonAsync("/api/items", newItem);
    
    // Assert - Create
    Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
    var createdItem = await createResponse.Content.ReadFromJsonAsync<Item>();
    
    // ==================== READ ====================
    // Act - Read
    var readResponse = await _client.GetAsync($"/api/items/{createdItem.Id}");
    
    // Assert - Read
    readResponse.EnsureSuccessStatusCode();
    
    // ==================== UPDATE ====================
    // Continue pattern...
}
```

#### HTTP Client Usage Patterns

**GET Requests:**
```csharp
var response = await _client.GetAsync("/api/endpoint");
response.EnsureSuccessStatusCode();
var result = await response.Content.ReadFromJsonAsync<T>();
```

**POST Requests:**
```csharp
var response = await _client.PostAsJsonAsync("/api/endpoint", data);
Assert.Equal(HttpStatusCode.Created, response.StatusCode);
var created = await response.Content.ReadFromJsonAsync<T>();
```

**PUT Requests:**
```csharp
var response = await _client.PutAsJsonAsync($"/api/endpoint/{id}", data);
response.EnsureSuccessStatusCode();
```

**DELETE Requests:**
```csharp
var response = await _client.DeleteAsync($"/api/endpoint/{id}");
Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
```

### Comprehensive Test Coverage Checklist

For each REST endpoint, create tests covering:

#### GET Collection
- ? Returns successfully with empty list
- ? Returns all items after creating multiple

#### GET by ID
- ? Returns item when it exists
- ? Returns 404 NotFound when item doesn't exist
- ? Returns 400 BadRequest with invalid ID format

#### POST (Create)
- ? Returns created item with valid data
- ? Returns 400 BadRequest with invalid/missing required data
- ? Returns 403 Forbidden when resource limits are reached
- ? Validates auto-generated fields (IDs, timestamps)

#### PUT (Update)
- ? Returns updated item when item exists
- ? Returns 404 NotFound when item doesn't exist
- ? Returns 400 BadRequest with invalid ID format
- ? Returns 400 BadRequest with invalid/missing data

#### DELETE
- ? Returns 204 NoContent when item exists
- ? Verifies item is actually deleted (follow-up GET returns 404)
- ? Returns 404 NotFound when item doesn't exist
- ? Returns 400 BadRequest with invalid ID format

#### Full CRUD Cycle Test
Always include ONE comprehensive test that:
1. **Creates** an item
2. **Reads** and validates it exists
3. **Updates** the item
4. **Reads** again and validates the update
5. **Deletes** the item
6. **Reads** again and validates 404
7. **Lists** all items and validates it's not present

### Package Requirements for Integration Tests

```xml
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
<PackageReference Include="xunit" Version="2.9.3" />
<PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
<PackageReference Include="coverlet.collector" Version="6.0.4" />
```

## Code Style & Conventions

### General Guidelines
- Use explicit types over `var` when it improves readability
- Use descriptive variable names (e.g., `createdMovie`, `updateResponse`)
- Add comments only for complex logic; let the code be self-documenting
- Follow Arrange-Act-Assert pattern in tests

### Model Validation
When models use Data Annotations:
```csharp
[Required(ErrorMessage = "Name is required")]
[StringLength(50, ErrorMessage = "Name cannot exceed 50 characters")]
public string Name { get; set; } = string.Empty;
```

Always test:
- Missing required fields return 400 BadRequest
- Validation error messages are appropriate

### GUID Handling
- Always validate GUID format with `Guid.TryParse()`
- Return 400 BadRequest for invalid GUID formats
- Use `Guid.NewGuid()` for auto-generation
- Store GUIDs as strings in models for API serialization

### DateTimeOffset Usage
Use `DateTimeOffset` instead of `DateTime` for timestamps:
```csharp
PublishDate = new DateTimeOffset(1999, 3, 31, 0, 0, 0, TimeSpan.Zero)
```

## Configuration Management

### ASP.NET Core Configuration Binding Pattern

**1. Create Configuration Class:**
```csharp
namespace YourProject.CustomSettings
{
    public class YourSettings
    {
        public int MaxItems { get; set; } = 10;
        public string SettingName { get; set; } = "DefaultValue";
    }
}
```

**2. Add to appsettings.json:**
```json
{
  "YourSettings": {
    "MaxItems": 10,
    "SettingName": "CustomValue"
  }
}
```

**3. Register in Program.cs:**
```csharp
// Bind settings from configuration
var yourSettings = builder.Configuration.GetSection("YourSettings").Get<YourSettings>() 
    ?? new YourSettings();
builder.Services.AddSingleton(yourSettings);
logger.LogInformation("YourSettings loaded: MaxItems = {MaxItems}", yourSettings.MaxItems);
```

**4. Inject into Controller:**
```csharp
public class YourController : ControllerBase
{
    private readonly YourSettings _settings;
    
    public YourController(YourSettings settings)
    {
        _settings = settings;
    }
    
    [HttpPost]
    public ActionResult Post()
    {
        if (items.Count >= _settings.MaxItems)
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Limit reached",
                detail: $"Maximum limit reached: [{_settings.MaxItems}]"
            );
        }
        // ... rest of logic
    }
}
```

### Azure App Service Environment Variables

**Nested Configuration Hierarchy:**
- In `appsettings.json`: Use `:` for nesting (e.g., `"Section:SubSection:Key"`)
- In Azure App Service: Use `__` (double underscore) for nesting (e.g., `Section__SubSection__Key`)

**Example Mapping:**
- Local config: `NoteLimits:MaxNotes`
- Azure env var: `NoteLimits__MaxNotes`

**Setting Environment Variables in Azure:**

1. **Azure Portal:**
   - Navigate to App Service ? Settings ? Environment variables
   - Add new application setting: `Section__Key` = `Value`
   - Apply and restart the app service

2. **Azure CLI:**
   ```bash
   az webapp config appsettings set \
     --name your-app-name \
     --resource-group your-rg \
     --settings Section__Key=Value
   ```

3. **Azure PowerShell:**
   ```powershell
   Set-AzWebApp -ResourceGroupName your-rg `
     -Name your-app-name `
     -AppSettings @{"Section__Key"="Value"}
   ```

**Best Practices:**
- Always provide sensible defaults in the configuration class
- Log configuration values during startup (except secrets)
- Use the null-coalescing operator (`??`) to fall back to defaults if binding fails
- For sensitive values, use Azure Key Vault or User Secrets (local dev)

### Resource Limit Patterns

When implementing resource limits (max items, rate limiting, etc.):

**1. Check limit BEFORE performing expensive operations:**
```csharp
// Check count first
if (items.Count >= _limits.MaxItems)
{
    return Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "Resource limit reached",
        detail: $"Limit reached: [{_limits.MaxItems}]"
    );
}

// Only then perform expensive operations (AI calls, database writes, etc.)
var result = await ExpensiveOperation();
items.Add(result);
```

**2. Return 403 Forbidden for limit violations:**
- Use `Problem()` helper to create ProblemDetails response
- Include the current limit value in the detail message
- Use a clear, user-friendly title

**3. Update API Documentation:**
- Add `ProducesResponseType` for 403 status code
- Document the limit behavior in XML comments
- Update Swagger/OpenAPI documentation

**4. Test the limit behavior:**
- Create a test that fills up to the limit
- Verify 403 is returned when attempting to exceed
- Verify the ProblemDetails structure matches specification

## Troubleshooting Common Issues

### Issue: "Program class not found" in tests
**Solution**: Ensure:
1. Program class is `public partial`
2. `InternalsVisibleTo` is configured in main project
3. Use fully qualified namespace: `WebApplicationFactory<YourNamespace.Program>`

### Issue: Tests fail due to state pollution
**Solution**: 
1. Create `CustomWebApplicationFactory` with state clearing
2. Call clear method in test constructor
3. Consider using `IAsyncLifetime` for async setup/cleanup

### Issue: xUnit runs tests in parallel causing conflicts
**Solution**: Use `CustomWebApplicationFactory` pattern to isolate state, or use:
```csharp
[Collection("Sequential")]
public class YourTestClass { }
```

## Running Tests

```bash
# Run all tests
dotnet test

# Run tests with detailed output
dotnet test --logger "console;verbosity=normal"

# Run specific test
dotnet test --filter "Name~FullCrudCycle"

# Run with coverage
dotnet test /p:CollectCoverage=true
```

## Project-Specific Notes

### Movie Model Properties
- `GuidId`: Auto-generated, stored as string
- `Name`: Required, max 50 characters
- `Category`: Required
- `PublishDate`: Required, use DateTimeOffset

### API Endpoints
- `GET /api/movies` - Get all movies
- `GET /api/movies/{id}` - Get movie by ID
- `POST /api/movies` - Create movie
- `PUT /api/movies/{id}` - Update movie
- `DELETE /api/movies/{id}` - Delete movie

## When Creating New Tests

1. **Always read the controller code first** to understand endpoints and behavior
2. **Check the model** to understand validation rules and required fields
3. **Create CustomWebApplicationFactory** if controller uses static state
4. **Follow the naming convention** for test methods
5. **Cover all HTTP status codes** the endpoint can return
6. **Include a full CRUD cycle test** for comprehensive validation
7. **Verify the test passes** before considering the work complete

## Reusing in Other Projects

To use these patterns in other ASP.NET Core projects:

1. **Copy `.github/copilot-instructions.md`** to your project root or `.github` folder
2. **Update project-specific sections**:
   - Project Overview
   - Model properties
   - API endpoints
   - Package versions (if different)
3. **Ensure main project has**:
   - `public partial class Program`
   - `InternalsVisibleTo` for test projects
4. **Create test project with**:
   - `Microsoft.AspNetCore.Mvc.Testing` package
   - `CustomWebApplicationFactory` (if needed)
   - Reference to main project

## Additional Resources

- [Integration tests in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests)
- [xUnit Documentation](https://xunit.net/)
- [WebApplicationFactory Documentation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1)

## End-to-End Testing Guidelines

- **Never run E2E tests (NoteKeeperControllerE2ETests) automatically.** E2E tests point to a live Azure production URL and require the user to publish the project first. Always ask the user to publish before E2E tests are run, and only run integration tests (NoteKeeperControllerIntegrationTests) automatically.
