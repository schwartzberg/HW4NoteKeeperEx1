using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using FluentAssertions;
using HW4NoteKeeperEx1.Data;
using HW4NoteKeeperEx1.RequestAndResultObjects;
using HW4NoteKeeperEx1.Services;
using HW4NoteKeeperEx1.Settings;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace HW4NoteKeeperEx1.Tests
{
    /// <summary>
    /// End-to-end tests for database and Azure Blob Storage seeding through DbInitializer.
    /// Tests verify that seeding always wipes existing data and creates 4 notes with corresponding containers.
    /// </summary>
    /// <remarks>
    /// IMPORTANT: These tests run against live Azure services (deployed API + Azure Blob Storage + Azure SQL Database)
    /// and must NOT run in parallel with other tests.
    /// </remarks>
    [Collection("Sequential")]
    [Trait("Category", "E2E")]
    public class NoteKeeperSeedingTests : IAsyncLifetime
    {
        private static readonly string BaseUrl =
            "https://app-notekeeper-cscie94-ps-hw4-ex1-fzb7ffggbqhhcybz.swedencentral-01.azurewebsites.net/";

        /// <summary>
        /// Azure-managed containers that survive seeding (e.g. Function App deployment package and runtime containers).
        /// These are excluded when asserting the seeded container count.
        /// </summary>
        private static readonly HashSet<string> _protectedContainers = new(StringComparer.OrdinalIgnoreCase)
        {
            "app-package-func-hw4",
            "azure-webjobs-hosts",
            "azure-webjobs-secrets"
        };

        private readonly HttpClient _client;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly MyDatabaseContext _context;
        private readonly IAzureStorageInitializer _storageInitializer;
        private readonly DbInitializer _dbInitializer;

        public NoteKeeperSeedingTests()
        {
            _client = new HttpClient { BaseAddress = new Uri(BaseUrl) };

            // Build configuration from user secrets
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddUserSecrets<NoteKeeperSeedingTests>()
                .Build();

            string storageUrl = config["StorageAccountSettings:Url"]!;
            string tenantId = config["StorageAccountSettings:TenantId"]!;

            // Use the same credential scoping pattern as the main app
            var credentialOptions = new DefaultAzureCredentialOptions
            {
                SharedTokenCacheTenantId = tenantId,
                VisualStudioCodeTenantId = tenantId,
                VisualStudioTenantId = tenantId,
                ExcludeEnvironmentCredential = true,
                ExcludeManagedIdentityCredential = true,
                ExcludeWorkloadIdentityCredential = true,
                ExcludeInteractiveBrowserCredential = true
            };

            var credential = new DefaultAzureCredential(credentialOptions);

            _blobServiceClient = new BlobServiceClient(
                new Uri(storageUrl),
                credential);

            string queueUrl = storageUrl.Replace(".blob.", ".queue.", StringComparison.OrdinalIgnoreCase);
            var queueServiceClient = new QueueServiceClient(new Uri(queueUrl), credential);

            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Set up database context
            string connectionString = config.GetConnectionString("DefaultConnection")!;
            var dbOptions = new DbContextOptionsBuilder<MyDatabaseContext>()
                .UseSqlServer(connectionString)
                .Options;
            _context = new MyDatabaseContext(dbOptions);

            // Set up Azure Storage Initializer
            var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<AzureStorageInitializer>();
            var telClient = new TelemetryClient(new Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration());
            var storageOpsSettings = config.GetSection("StorageOperationalSettings").Get<HW4NoteKeeperEx1.Settings.StorageOperationalSettings>()
                ?? new HW4NoteKeeperEx1.Settings.StorageOperationalSettings();
            _storageInitializer = new AzureStorageInitializer(
                _blobServiceClient, queueServiceClient,
                CreateJobsTableService(config),
                storageOpsSettings, logger, telClient);

            // Set up DbInitializer (we'll use this in tests to trigger seeding)
            var aiSettings = config.GetSection("AzureOpenAI").Get<AISettings>()!;
            Uri openAIUri = new Uri(aiSettings.DeploymentUri);
            var apiKeyCred = new Azure.AzureKeyCredential(aiSettings.ApiKey);
            var azureOpenAiClient = new Azure.AI.OpenAI.AzureOpenAIClient(openAIUri, apiKeyCred);
            var chatClient = azureOpenAiClient.GetChatClient(aiSettings.DeploymentModelName).AsIChatClient();

            var dbLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<DbInitializer>();
            _dbInitializer = new DbInitializer(_context, chatClient, aiSettings, dbLogger, telClient, _storageInitializer);
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            _context?.Dispose();
            _client?.Dispose();
            await Task.CompletedTask;
        }

        #region Seeding Behavior Tests

        /// <summary>
        /// Verifies that DbInitializer deletes all containers before seeding.
        /// </summary>
        [Fact]
        public async Task Seeding_DeletesAllContainersBeforeSeeding()
        {
            // Arrange: Create a test container that should be deleted
            string testContainerName = Guid.NewGuid().ToString();
            var testContainer = _blobServiceClient.GetBlobContainerClient(testContainerName);
            await testContainer.CreateAsync();

            // Act: Run seeding
            await _dbInitializer.InitializeAsync();

            // Assert: Test container should be deleted
            bool exists = (await testContainer.ExistsAsync()).Value;
            exists.Should().BeFalse("Seeding should delete all containers including test containers");

            // Verify 4 seeded containers exist (excluding protected system containers)
            var containerCount = 0;
            await foreach (var container in _blobServiceClient.GetBlobContainersAsync())
            {
                if (!_protectedContainers.Contains(container.Name) && !container.Name.StartsWith("$"))
                    containerCount++;
            }
            containerCount.Should().Be(4, "Should have exactly 4 seeded containers");
        }

        /// <summary>
        /// Verifies that seeding creates exactly 4 notes in the database.
        /// </summary>
        [Fact]
        public async Task Seeding_CreatesExactly4Notes()
        {
            // Act: Run seeding
            await _dbInitializer.InitializeAsync();

            // Wait a moment for database consistency
            await Task.Delay(1000);

            // Assert: Check via API
            HttpResponseMessage response = await _client.GetAsync("/NoteKeeper");
            
            // Add better error diagnostics
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"API returned {response.StatusCode}. Body: {errorBody}");
            }
            
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            string jsonContent = await response.Content.ReadAsStringAsync();
            var notes = JsonSerializer.Deserialize<List<NoteResult>>(jsonContent, _jsonOptions);
            
            notes.Should().NotBeNull("Response should deserialize to a list of notes");
            notes!.Count.Should().Be(4, $"Seeding should create exactly 4 notes. Found {notes.Count}. Notes: {string.Join(", ", notes.Select(n => n.Summary))}");
        }

        /// <summary>
        /// Verifies that the 4 note GUIDs match the 4 container names in Azure Storage.
        /// </summary>
        [Fact]
        public async Task Seeding_NoteIdsMatchContainerNames()
        {
            // Act: Run seeding
            await _dbInitializer.InitializeAsync();

            // Wait a moment for consistency
            await Task.Delay(1000);

            // Get notes from API
            HttpResponseMessage response = await _client.GetAsync("/NoteKeeper");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            string jsonContent = await response.Content.ReadAsStringAsync();
            var notes = JsonSerializer.Deserialize<List<NoteResult>>(jsonContent, _jsonOptions);
            notes.Should().NotBeNull();

            // Get container names from Azure Storage (excluding protected system containers)
            var containerNames = new List<string>();
            await foreach (var container in _blobServiceClient.GetBlobContainersAsync())
            {
                if (!_protectedContainers.Contains(container.Name) && !container.Name.StartsWith("$"))
                    containerNames.Add(container.Name);
            }

            // Assert: Should have exactly 4 notes and 4 containers
            notes!.Count.Should().Be(4, $"Should have 4 notes. Found: {notes.Count}");
            containerNames.Count.Should().Be(4, $"Should have 4 containers. Found: {containerNames.Count}");

            // Assert: Every note ID should have a matching container
            foreach (var note in notes!)
            {
                string expectedContainerName = note.Id.ToString().ToLowerInvariant();
                containerNames.Should().Contain(expectedContainerName,
                    $"Container '{expectedContainerName}' should exist for note '{note.Summary}' (ID: {note.Id})");
            }
        }

        /// <summary>
        /// Verifies that each seeded note has the correct attachments/blobs in its container.
        /// </summary>
        [Fact]
        public async Task Seeding_EachContainerHasCorrectBlobs()
        {
            // Act: Run seeding
            await _dbInitializer.InitializeAsync();

            // Get notes from API
            HttpResponseMessage response = await _client.GetAsync("/NoteKeeper");
            var notes = await response.Content.ReadFromJsonAsync<List<NoteResult>>(_jsonOptions);
            notes.Should().NotBeNull();

            // Expected attachments per note summary
            var expectedAttachments = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "Running grocery list", new[] { "MilkAndEggs.png", "Oranges.png" } },
                { "Gift supplies notes", new[] { "WrappingPaper.png", "Tape.png" } },
                { "Valentine's Day gift ideas", new[] { "Chocolate.png", "Diamonds.png", "NewCar.png" } },
                { "Azure tips", new[] { "AzureLogo.png", "AzureTipsAndTricks.pdf" } }
            };

            // Verify each note's container has the correct blobs
            foreach (var note in notes!)
            {
                if (!expectedAttachments.TryGetValue(note.Summary, out string[]? expectedBlobs))
                    continue;

                string containerName = note.Id.ToString().ToLowerInvariant();
                var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

                // Get actual blobs
                var actualBlobs = new List<string>();
                await foreach (var blob in containerClient.GetBlobsAsync())
                {
                    actualBlobs.Add(blob.Name);
                }

                // Assert
                actualBlobs.Should().BeEquivalentTo(expectedBlobs,
                    $"Container for note '{note.Summary}' should have the correct blobs");
            }
        }

        /// <summary>
        /// Verifies that "Running grocery list" note has its container with expected blobs.
        /// </summary>
        [Fact]
        public async Task Seeding_GroceryListNote_HasExpectedBlobsInStorage()
        {
            // Act: Run seeding
            await _dbInitializer.InitializeAsync();

            // Find the seeded grocery list note via API
            HttpResponseMessage response = await _client.GetAsync("/NoteKeeper");
            response.EnsureSuccessStatusCode();
            var notes = await response.Content.ReadFromJsonAsync<List<NoteResult>>(_jsonOptions);
            notes.Should().NotBeNull();

            var groceryNote = notes!.FirstOrDefault(n =>
                n.Summary != null &&
                n.Summary.Equals("Running grocery list", StringComparison.OrdinalIgnoreCase));

            groceryNote.Should().NotBeNull("'Running grocery list' note should be seeded");

            string containerId = groceryNote!.Id.ToString().ToLowerInvariant();
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerId);

            bool containerExists = (await containerClient.ExistsAsync()).Value;
            containerExists.Should().BeTrue(
                $"Container '{containerId}' should exist for the 'Running grocery list' note");

            var blobNames = new List<string>();
            await foreach (var blob in containerClient.GetBlobsAsync())
                blobNames.Add(blob.Name);

            blobNames.Should().Contain("MilkAndEggs.png");
            blobNames.Should().Contain("Oranges.png");
        }

        /// <summary>
        /// Verifies that all four seeded notes have their blob containers in Azure Storage.
        /// </summary>
        [Fact]
        public async Task Seeding_AllFourNotes_HaveContainersInStorage()
        {
            // Act: Run seeding
            await _dbInitializer.InitializeAsync();

            // Get notes from API
            HttpResponseMessage response = await _client.GetAsync("/NoteKeeper");
            response.EnsureSuccessStatusCode();
            var notes = await response.Content.ReadFromJsonAsync<List<NoteResult>>(_jsonOptions);
            notes.Should().NotBeNull();

            string[] seededSummaries = new[]
            {
                "Running grocery list",
                "Gift supplies notes",
                "Valentine's Day gift ideas",
                "Azure tips"
            };

            foreach (string summary in seededSummaries)
            {
                var note = notes!.FirstOrDefault(n =>
                    n.Summary != null &&
                    n.Summary.Equals(summary, StringComparison.OrdinalIgnoreCase));

                note.Should().NotBeNull($"Note '{summary}' should be seeded");

                var containerClient = _blobServiceClient.GetBlobContainerClient(note!.Id.ToString().ToLowerInvariant());
                bool exists = (await containerClient.ExistsAsync()).Value;
                exists.Should().BeTrue(
                    $"Container for note '{summary}' (id={note.Id}) should exist after seeding");
            }
        }

        /// <summary>
        /// Verifies that re-running seeding wipes and recreates all data (idempotent seeding).
        /// </summary>
        [Fact]
        public async Task Seeding_RunTwice_WipesAndRecreatesData()
        {
            // Act: Run seeding first time
            await _dbInitializer.InitializeAsync();

            // Get first set of note IDs
            var response1 = await _client.GetAsync("/NoteKeeper");
            var notes1 = await response1.Content.ReadFromJsonAsync<List<NoteResult>>(_jsonOptions);
            var firstNoteIds = notes1!.Select(n => n.Id).ToList();

            // Act: Run seeding second time
            await _dbInitializer.InitializeAsync();

            // Get second set of note IDs
            var response2 = await _client.GetAsync("/NoteKeeper");
            var notes2 = await response2.Content.ReadFromJsonAsync<List<NoteResult>>(_jsonOptions);
            var secondNoteIds = notes2!.Select(n => n.Id).ToList();

            // Assert: IDs should be different (new GUIDs generated)
            secondNoteIds.Should().NotBeEquivalentTo(firstNoteIds,
                "Seeding should generate new GUIDs each time");

            // Assert: Still exactly 4 notes
            notes2.Count.Should().Be(4, "Should still have exactly 4 notes after re-seeding");

            // Assert: Still exactly 4 containers (excluding protected system containers)
            var containerCount = 0;
            await foreach (var container in _blobServiceClient.GetBlobContainersAsync())
            {
                if (!_protectedContainers.Contains(container.Name) && !container.Name.StartsWith("$"))
                    containerCount++;
            }
            containerCount.Should().Be(4, "Should still have exactly 4 containers after re-seeding");
        }

        /// <summary>
        /// Verifies that notes created via the API (e.g. during E2E testing) are removed by seeding.
        /// This is the exact scenario that caused 6 records instead of 4 after redeployment.
        /// </summary>
        [Fact]
        public async Task Seeding_RemovesExtraNotes_CreatedViaAPI()
        {
            // Arrange: Run seeding first to get a clean baseline
            await _dbInitializer.InitializeAsync();

            // Create 2 extra notes via API (simulates E2E test pollution)
            var extraNote1 = new { Summary = "E2E Test Note", Details = "Should be removed by seeding" };
            var extraNote2 = new { Summary = "E2E Test Note 2", Details = "Also should be removed" };
            await _client.PostAsJsonAsync("/NoteKeeper", extraNote1);
            await _client.PostAsJsonAsync("/NoteKeeper", extraNote2);

            // Verify we now have 6 notes
            var beforeResponse = await _client.GetAsync("/NoteKeeper");
            var beforeNotes = await beforeResponse.Content.ReadFromJsonAsync<List<NoteResult>>(_jsonOptions);
            beforeNotes!.Count.Should().Be(6, "Should have 6 notes (4 seed + 2 extra) before re-seeding");

            // Act: Re-run seeding
            await _dbInitializer.InitializeAsync();
            await Task.Delay(1000);

            // Assert: Only 4 seed notes remain
            var afterResponse = await _client.GetAsync("/NoteKeeper");
            var afterNotes = await afterResponse.Content.ReadFromJsonAsync<List<NoteResult>>(_jsonOptions);
            afterNotes!.Count.Should().Be(4, "Seeding must remove extra notes — only 4 seed notes should remain");

            // Verify none of the extra notes survived
            afterNotes.Should().NotContain(n => n.Summary == "E2E Test Note");
            afterNotes.Should().NotContain(n => n.Summary == "E2E Test Note 2");
        }

        /// <summary>
        /// Verifies that -zip containers created by Azure Functions are removed by seeding.
        /// This covers the scenario where zip containers from E2E testing survived redeployment.
        /// </summary>
        [Fact]
        public async Task Seeding_RemovesZipContainers_CreatedByFunctions()
        {
            // Arrange: Create a fake -zip container (simulates Azure Function creating zip output)
            string fakeNoteId = Guid.NewGuid().ToString();
            string zipContainerName = $"{fakeNoteId}-zip";
            var zipContainer = _blobServiceClient.GetBlobContainerClient(zipContainerName);
            await zipContainer.CreateAsync();

            bool existsBefore = (await zipContainer.ExistsAsync()).Value;
            existsBefore.Should().BeTrue("Zip container should exist before seeding");

            // Act: Run seeding
            await _dbInitializer.InitializeAsync();

            // Assert: -zip container should be deleted
            bool existsAfter = (await zipContainer.ExistsAsync()).Value;
            existsAfter.Should().BeFalse("Seeding must delete -zip containers created by functions");
        }

        /// <summary>
        /// Verifies that seeding clears all 4 queues (ex1, ex1-poison, legacy, legacy-poison).
        /// </summary>
        [Fact]
        public async Task Seeding_ClearsAllFourQueues()
        {
            // Arrange: Get operational settings to know queue names
            var settings = new StorageOperationalSettings();
            string[] queueNames = new[]
            {
                settings.ZipRequestsQueueName,
                settings.ZipPoisonQueueName,
                settings.ZipRequestsLegacyQueueName,
                settings.ZipRequestsLegacyPoisonQueueName
            };

            // Enqueue a test message in each queue
            string queueUrl = _blobServiceClient.Uri.ToString()
                .Replace(".blob.", ".queue.", StringComparison.OrdinalIgnoreCase);
            var queueServiceClient = new QueueServiceClient(
                new Uri(queueUrl),
                new DefaultAzureCredential());

            foreach (string queueName in queueNames)
            {
                var queueClient = queueServiceClient.GetQueueClient(queueName);
                await queueClient.CreateIfNotExistsAsync();
                await queueClient.SendMessageAsync("test-seeding-cleanup");
            }

            // Act: Run seeding
            await _dbInitializer.InitializeAsync();

            // Assert: All queues should be empty
            foreach (string queueName in queueNames)
            {
                var queueClient = queueServiceClient.GetQueueClient(queueName);
                if ((await queueClient.ExistsAsync()).Value)
                {
                    var props = await queueClient.GetPropertiesAsync();
                    props.Value.ApproximateMessagesCount.Should().Be(0,
                        $"Queue '{queueName}' should be empty after seeding");
                }
            }
        }

        /// <summary>
        /// Verifies that seeding clears all rows from the Jobs table.
        /// </summary>
        [Fact]
        public async Task Seeding_ClearsJobsTable()
        {
            // Arrange: Insert a test job row
            string testNoteId = Guid.NewGuid().ToString();
            string testZipFileId = "test-seeding-cleanup.zip";

            var entity = new TableEntity(testNoteId, testZipFileId)
            {
                { "Status", "Queued" },
                { "StatusDetails", "Test row for seeding cleanup" }
            };

            var tableServiceUri = new Uri($"https://st4hw3.table.core.windows.net");
            var tableClient = new TableServiceClient(tableServiceUri, new DefaultAzureCredential())
                .GetTableClient("Jobs");
            await tableClient.UpsertEntityAsync(entity);

            // Act: Run seeding
            await _dbInitializer.InitializeAsync();

            // Assert: The test row should be gone
            try
            {
                await tableClient.GetEntityAsync<TableEntity>(testNoteId, testZipFileId);
                Assert.Fail("Job row should have been deleted by seeding");
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                // Expected — row was deleted
            }
        }

        /// <summary>
        /// Verifies that containers created for notes added via the API are deleted after re-seeding.
        /// Covers the scenario where E2E test note containers persisted across deployments.
        /// </summary>
        [Fact]
        public async Task Seeding_RemovesContainers_ForAPICreatedNotes()
        {
            // Arrange: Create a note via API (creates a container)
            var extraNote = new { Summary = "Temp test note", Details = "Container should not survive seeding" };
            var createResp = await _client.PostAsJsonAsync("/NoteKeeper", extraNote);
            createResp.EnsureSuccessStatusCode();
            var created = await createResp.Content.ReadFromJsonAsync<NoteResult>(_jsonOptions);

            // Upload an attachment to force container creation
            string noteId = created!.Id.ToString().ToLowerInvariant();
            var containerClient = _blobServiceClient.GetBlobContainerClient(noteId);

            // Container may or may not exist yet (depends on whether PUT attachment was called).
            // Create it explicitly to simulate real usage.
            await containerClient.CreateIfNotExistsAsync();

            bool existsBefore = (await containerClient.ExistsAsync()).Value;
            existsBefore.Should().BeTrue("Container for API-created note should exist before seeding");

            // Act: Re-run seeding
            await _dbInitializer.InitializeAsync();

            // Assert: Extra note container should be deleted
            bool existsAfter = (await containerClient.ExistsAsync()).Value;
            existsAfter.Should().BeFalse(
                $"Container '{noteId}' for API-created note should be deleted by seeding");

            // Assert: Only 4 seed containers remain
            int containerCount = 0;
            await foreach (var container in _blobServiceClient.GetBlobContainersAsync())
            {
                if (!_protectedContainers.Contains(container.Name) && !container.Name.StartsWith("$"))
                    containerCount++;
            }
            containerCount.Should().Be(4, "Only 4 seed containers should remain after seeding");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Creates a <see cref="JobsTableService"/> for test use, constructing a <see cref="TableClient"/>
        /// from config (same approach as the Web API's Program.cs).
        /// </summary>
        private static JobsTableService CreateJobsTableService(IConfiguration config)
        {
            var storageSettings = config.GetSection("StorageAccountSettings").Get<StorageAccountSettings>();
            string accountName = storageSettings?.AccountName ?? "st4hw3";
            string tenantId = storageSettings?.TenantId ?? string.Empty;
            string jobsTableName = config["StorageOperationalSettings:JobsTableName"] ?? "Jobs";

            var credentialOptions = new DefaultAzureCredentialOptions
            {
                SharedTokenCacheTenantId = tenantId,
                VisualStudioCodeTenantId = tenantId,
                VisualStudioTenantId = tenantId,
                ExcludeEnvironmentCredential = true,
                ExcludeManagedIdentityCredential = true,
                ExcludeWorkloadIdentityCredential = true,
                ExcludeInteractiveBrowserCredential = true
            };
            var credential = new DefaultAzureCredential(credentialOptions);
            var tableServiceUri = new Uri($"https://{accountName}.table.core.windows.net");
            var tableServiceClient = new TableServiceClient(tableServiceUri, credential);
            var tableClient = tableServiceClient.GetTableClient(jobsTableName);

            var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<JobsTableService>();
            return new JobsTableService(tableClient, logger);
        }

        #endregion
    }
}
