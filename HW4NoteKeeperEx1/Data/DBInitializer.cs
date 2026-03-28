 
 
using HW4NoteKeeperEx1.Models;
using HW4NoteKeeperEx1.Services;
using HW4NoteKeeperEx1.Settings;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Abstractions;

namespace HW4NoteKeeperEx1.Data
{
    /// <summary>
    /// Initializes (seeds) the database and Azure Blob Storage with data.
    /// Always wipes existing data and reseeds on application startup.
    /// </summary>
    /// <remarks>Step 7</remarks>
    public class DbInitializer
    {
        private readonly MyDatabaseContext _context;
        private readonly IChatClient _chatClient;
        private readonly AISettings _aiSettings;
        private readonly ILogger<DbInitializer> _logger;
        private readonly TelemetryClient _telClient;
        private readonly IAzureStorageInitializer _storageInitializer;

        public DbInitializer(
            MyDatabaseContext context,
            IChatClient chatClient,
            AISettings aiSettings,
            ILogger<DbInitializer> logger,
            TelemetryClient telClient,
            IAzureStorageInitializer storageInitializer)
        {
            _context = context;
            _chatClient = chatClient;
            _aiSettings = aiSettings;
            _logger = logger;
            _telClient = telClient;
            _storageInitializer = storageInitializer;
        }

        /// <summary>
        /// Initializes the database and Azure Blob Storage by:
        /// 1. Deleting all existing notes/tags from the database
        /// 2. Deleting all containers from Azure Blob Storage
        /// 3. Seeding 4 default notes with tags
        /// 4. Creating blob containers and uploading attachments for each note
        /// </summary>
        public async Task InitializeAsync()
        {
            _logger.LogInformation("Starting database and storage initialization (always reseeds)...");

            // Step 1: Ensure database exists and apply any pending migrations
            _logger.LogInformation("Ensuring database exists and applying migrations...");
            await _context.Database.MigrateAsync();
            
            // Step 2: Clear existing data (instead of dropping database)
            _logger.LogInformation("Clearing existing notes and tags...");
            _context.Tags.RemoveRange(_context.Tags);
            _context.Notes.RemoveRange(_context.Notes);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Database cleared successfully.");

            // Step 3: Delete all containers from Azure Blob Storage
            await _storageInitializer.DeleteAllContainersAsync();

            // Step 3b: Clear the zip-requests and poison queues so stale messages don't fire post-deploy
            await _storageInitializer.ClearQueuesAsync();

            // Step 4: Seed the 4 default notes
            _logger.LogInformation("Seeding default notes...");

            // Create the tag generator service
            var tagGeneratorService = new TagGeneratorService(_chatClient, _aiSettings, _logger);

            // Define the 4 default notes to seed
            var seedData = new[]
            {
                new { Summary = "Running grocery list", Details = "Milk, Eggs, Oranges" },
                new { Summary = "Gift supplies notes", Details = "Tape & Wrapping Paper" },
                new { Summary = "Valentine's Day gift ideas", Details = "Chocolate, Diamonds, New car" },
                new { Summary = "Azure tips", Details = "portal.azure.com is a quick way to get to the portal. Remember double underscore for Linux and colon for windows" }
            };

            var seedCount = 0;
            foreach (var seed in seedData)
            {
                try
                {
                    // Generate tags using AI
                    var tagResponse = await tagGeneratorService.GenerateTags(seed.Details);

                    var note = new Note
                    {
                        Id = Guid.NewGuid(),
                        Summary = seed.Summary,
                        Details = seed.Details,
                        CreatedDateUtc = DateTimeOffset.UtcNow,
                        ModifiedDateUtc = null
                    };

                    seedCount++;

                    // Add the note to context
                    _context.Notes.Add(note);

                    var tagCount = 0;
                    // Add tags for this note
                    if (tagResponse.Tags != null && tagResponse.Tags.Count > 0)
                    {
                        foreach (var tagName in tagResponse.Tags)
                        {
                            // Truncate tag name if longer than 30 characters
                            string truncatedTagName = tagName.Length > 30 ? tagName.Substring(0, 30) : tagName;

                            var tag = new Tag
                            {
                                Id = Guid.NewGuid(),
                                NoteId = note.Id,
                                Name = truncatedTagName
                            };
                            _context.Tags.Add(tag);
                            tagCount++;
                        }
                    }

                    // Save the note to the database BEFORE creating the container
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Seeded note: {Summary} (ID: {NoteId}) with {TagCount} tags",
                        seed.Summary, note.Id, tagResponse.Tags?.Count ?? 0);

                    _telClient.TrackEvent("NoteKeeper azure db updated with seed",
                        properties: new Dictionary<string, string>()
                        {
                            { "Seed summary", seed.Summary },
                            { "Seed count", seedCount.ToString() },
                            { "Tag count", tagCount.ToString() },
                            { "NoteId", note.Id.ToString() }
                        });

                    // Step 5: Create blob container and upload attachments for this note
                    bool storageSuccess = await _storageInitializer.InitializeAsync(note.Id, seed.Summary);
                    if (!storageSuccess)
                    {
                        _logger.LogWarning("Failed to seed Azure Storage for note '{Summary}' (ID: {NoteId}). Continuing with next note.",
                            seed.Summary, note.Id);
                    }
                }
                catch (Exception ex)
                {
                    _telClient.TrackException(ex,
                        properties: new Dictionary<string, string>()
                        {
                            { "Seed counts created", seedCount.ToString() },
                            { "Exception with seeding", ex.Message }
                        });

                    _logger.LogError(ex, "Error seeding note: {Summary}", seed.Summary);
                    // Continue with next note
                }
            }

            _logger.LogInformation("Database and storage initialization completed successfully. Seeded {Count} note(s).", seedCount);
        }
    }
}

