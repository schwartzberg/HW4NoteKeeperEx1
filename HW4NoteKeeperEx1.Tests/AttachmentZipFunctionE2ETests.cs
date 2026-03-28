using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace HW4NoteKeeperEx1.Tests
{
    /// <summary>
    /// End-to-end tests for the <c>AttachmentZipFunction</c> Azure Function.
    /// Each test that expects a zip to be created first creates a Note via the live API
    /// (so the note ID exists in the SQL database), uploads PNG attachments via the live API
    /// (so the attachment container exists in blob storage), then enqueues a message
    /// and waits for the function to produce the zip blob.
    /// </summary>
    /// <remarks>
    /// IMPORTANT: Never run these tests automatically – they require both the API and the
    /// Azure Function App to be deployed.  Run only after publishing with
    /// <c>dotnet test --filter "Category=E2E"</c>.
    /// </remarks>
    [Collection("Sequential")]
    [Trait("Category", "E2E")]
    public class AttachmentZipFunctionE2ETests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
    {
        private const string QueueName = "attachment-zip-requests";
        private const string StorageAccountName = "st4hw3";
        private static readonly string BaseUrl =
            "https://app-notekeeper-cscie94-ps-hw4-bdffa3cmetfag8em.swedencentral-01.azurewebsites.net/";

        private readonly HttpClient _apiClient;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly QueueServiceClient _queueServiceClient;
        private readonly JsonSerializerOptions _jsonOptions;

        // Notes created via the API – cleaned up with the enhanced DELETE (removes DB row + storage)
        private readonly List<string> _createdNoteIds = new();
        // Containers created directly in storage (e.g. zip containers) that need direct cleanup
        private readonly List<string> _containerNamesToDelete = new();

        public AttachmentZipFunctionE2ETests(WebApplicationFactory<Program> factory)
        {
            _apiClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };

            IConfiguration config = new ConfigurationBuilder()
                .AddUserSecrets<AttachmentZipFunctionE2ETests>()
                .Build();

            string storageUrl = config["StorageAccountSettings:Url"]!;
            string tenantId = config["StorageAccountSettings:TenantId"]!;

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

            _blobServiceClient = new BlobServiceClient(new Uri(storageUrl), credential);
            _queueServiceClient = new QueueServiceClient(
                new Uri($"https://{StorageAccountName}.queue.core.windows.net"),
                credential,
                new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });

            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            // Use the enhanced DELETE (notes/{noteId}) which removes the note+tags from DB
            // AND deletes both the attachment container ({noteId}) and the zip container ({noteId}-zip).
            foreach (string noteId in _createdNoteIds)
            {
                try { await _apiClient.DeleteAsync($"notes/{noteId}"); }
                catch { /* best-effort cleanup */ }
            }

            // Belt-and-suspenders: directly delete any tracked containers in case the API call above
            // failed or the zip container was not yet associated with a note.
            foreach (string containerName in _containerNamesToDelete)
            {
                try { await _blobServiceClient.GetBlobContainerClient(containerName).DeleteIfExistsAsync(); }
                catch { /* best-effort cleanup */ }
            }
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a Note via the live API (POST /NoteKeeper) so the note ID is in the SQL database.
        /// Returns the new note's GUID as a string.
        /// </summary>
        private async Task<string> CreateTestNoteAsync()
        {
            var noteInput = new
            {
                summary = "Zip Function E2E Test",
                details = "Test note created by AttachmentZipFunctionE2ETests for zip creation validation"
            };

            var response = await _apiClient.PostAsJsonAsync("NoteKeeper", noteInput);
            response.StatusCode.Should().Be(HttpStatusCode.Created,
                $"Failed to create test note: {await response.Content.ReadAsStringAsync()}");

            string body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            string noteId = doc.RootElement.GetProperty("noteId").GetString()!;
            _createdNoteIds.Add(noteId);

            // Track the attachment container (named by noteId) for direct cleanup in DisposeAsync,
            // so it is deleted even if the enhanced DELETE endpoint fails to clean up storage.
            _containerNamesToDelete.Add(noteId.ToLower());

            return noteId;
        }

        /// <summary>
        /// Uploads a PNG file from <c>AzureStorageAttachments\</c> via the live API
        /// (PUT /notes/{noteId}/attachments/{fileName} multipart/form-data).
        /// This creates the attachment container in blob storage under the note ID.
        /// </summary>
        private async Task UploadPngAttachmentAsync(string noteId, string fileName)
        {
            string pngPath = Path.Combine(AppContext.BaseDirectory, "AzureStorageAttachments", fileName);
            byte[] bytes = await File.ReadAllBytesAsync(pngPath);

            using var form = new MultipartFormDataContent();
            using var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(fileContent, "fileData", fileName);

            var response = await _apiClient.PutAsync($"notes/{noteId}/attachments/{fileName}", form);
            response.StatusCode.Should().BeOneOf(
                HttpStatusCode.Created, HttpStatusCode.NoContent);
        }

        /// <summary>Sends a ZipRequest message to the attachment-zip-requests queue.</summary>
        private async Task EnqueueZipRequestAsync(string noteId, string zipFileId)
        {
            var message = new { noteId, zipFileId };
            string json = JsonSerializer.Serialize(message);
            var queueClient = _queueServiceClient.GetQueueClient(QueueName);
            await queueClient.SendMessageAsync(json);
        }

        /// <summary>
        /// Polls until the expected zip blob exists in the <c>{noteId}-zip</c> container
        /// or the timeout expires.
        /// </summary>
        private async Task WaitForZipBlobAsync(string noteId, string zipFileId, int maxWaitSeconds = 90)
        {
            string zipContainerName = $"{noteId.ToLower()}-zip";
            var containerClient = _blobServiceClient.GetBlobContainerClient(zipContainerName);
            var blobClient = containerClient.GetBlobClient(zipFileId);

            DateTime deadline = DateTime.UtcNow.AddSeconds(maxWaitSeconds);
            while (DateTime.UtcNow < deadline)
            {
                if (await containerClient.ExistsAsync() && await blobClient.ExistsAsync())
                    return;
                await Task.Delay(3000);
            }

            throw new TimeoutException(
                $"Zip blob '{zipFileId}' did not appear in container '{zipContainerName}' within {maxWaitSeconds}s.");
        }

        // ─── Tests ───────────────────────────────────────────────────────────────

        [Fact]
        public async Task Function_CreatesZipBlob_WhenAttachmentContainerHasBlobs()
        {
            // Arrange – create note in DB + upload PNG attachments via the live API
            string noteId = await CreateTestNoteAsync();
            await UploadPngAttachmentAsync(noteId, "WrappingPaper.png");
            await UploadPngAttachmentAsync(noteId, "Tape.png");

            string zipFileId = $"{Guid.NewGuid()}.zip";
            string zipContainerName = $"{noteId.ToLower()}-zip";
            _containerNamesToDelete.Add(zipContainerName);

            // Act – enqueue message and wait for function to produce zip
            await EnqueueZipRequestAsync(noteId, zipFileId);
            await WaitForZipBlobAsync(noteId, zipFileId, maxWaitSeconds: 180);

            // Assert – zip container and blob both exist
            var zipContainer = _blobServiceClient.GetBlobContainerClient(zipContainerName);
            (await zipContainer.ExistsAsync()).Value.Should().BeTrue($"zip container '{zipContainerName}' should have been created");
            (await zipContainer.GetBlobClient(zipFileId).ExistsAsync()).Value.Should().BeTrue($"zip blob '{zipFileId}' should exist in the container");
        }

        [Fact]
        public async Task Function_CreatesZipContainingAllAttachmentBlobs()
        {
            // Arrange – create note + upload 3 PNG attachments via the API
            string noteId = await CreateTestNoteAsync();
            await UploadPngAttachmentAsync(noteId, "WrappingPaper.png");
            await UploadPngAttachmentAsync(noteId, "Tape.png");
            await UploadPngAttachmentAsync(noteId, "Oranges.png");

            string zipFileId = $"{Guid.NewGuid()}.zip";
            string zipContainerName = $"{noteId.ToLower()}-zip";
            _containerNamesToDelete.Add(zipContainerName);

            // Act
            await EnqueueZipRequestAsync(noteId, zipFileId);
            await WaitForZipBlobAsync(noteId, zipFileId, maxWaitSeconds: 90);

            // Assert – zip is a valid archive containing all 3 attachment files
            var zipBlobClient = _blobServiceClient.GetBlobContainerClient(zipContainerName).GetBlobClient(zipFileId);
            var download = await zipBlobClient.DownloadContentAsync();
            byte[] zipBytes = download.Value.Content.ToArray();
            zipBytes.Length.Should().BeGreaterThan(0);
            zipBytes[0].Should().Be(0x50, "first byte should be 'P' (PK magic)");
            zipBytes[1].Should().Be(0x4B, "second byte should be 'K' (PK magic)");

            using var zipStream = new MemoryStream(zipBytes);
            using var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read);
            archive.Entries.Select(e => e.Name).Should().Contain(
                new[] { "WrappingPaper.png", "Tape.png", "Oranges.png" });
        }

        [Fact]
        public async Task Function_SetsContentTypeToApplicationZip()
        {
            // Arrange – create note + one attachment via API
            string noteId = await CreateTestNoteAsync();
            await UploadPngAttachmentAsync(noteId, "WrappingPaper.png");

            string zipFileId = $"{Guid.NewGuid()}.zip";
            string zipContainerName = $"{noteId.ToLower()}-zip";
            _containerNamesToDelete.Add(zipContainerName);

            // Act
            await EnqueueZipRequestAsync(noteId, zipFileId);
            await WaitForZipBlobAsync(noteId, zipFileId, maxWaitSeconds: 90);

            // Assert – ContentType header is application/zip
            var zipBlobClient = _blobServiceClient.GetBlobContainerClient(zipContainerName).GetBlobClient(zipFileId);
            var properties = await zipBlobClient.GetPropertiesAsync();
            properties.Value.ContentType.Should().Be("application/zip");
        }

        [Fact]
        public async Task Function_DoesNotCreateZip_WhenAttachmentContainerIsEmpty()
        {
            // Arrange – create note in DB but do NOT upload any attachments
            // The attachment container will not exist (or be empty), so nothing to zip
            string noteId = await CreateTestNoteAsync();
            string zipFileId = $"{Guid.NewGuid()}.zip";
            string zipContainerName = $"{noteId.ToLower()}-zip";

            // Act – enqueue; function should log a warning and return without creating zip
            await EnqueueZipRequestAsync(noteId, zipFileId);
            await Task.Delay(30_000);

            var zipContainer = _blobServiceClient.GetBlobContainerClient(zipContainerName);
            bool zipContainerExists = await zipContainer.ExistsAsync();

            if (zipContainerExists)
            {
                bool blobExists = await zipContainer.GetBlobClient(zipFileId).ExistsAsync();
                blobExists.Should().BeFalse("zip blob should not be created when no attachments exist");
            }
            else
            {
                zipContainerExists.Should().BeFalse();
            }
        }

        [Fact]
        public async Task Function_DoesNotCreateZip_WhenNoteDoesNotExistInDatabase()
        {
            // Arrange – use a random noteId that is NOT in the database or storage
            string noteId = Guid.NewGuid().ToString();
            string zipFileId = $"{Guid.NewGuid()}.zip";
            string zipContainerName = $"{noteId.ToLower()}-zip";

            // Act – enqueue; function should discard after DB check finds no such note
            await EnqueueZipRequestAsync(noteId, zipFileId);
            await Task.Delay(30_000);

            var zipContainer = _blobServiceClient.GetBlobContainerClient(zipContainerName);
            bool zipContainerExists = await zipContainer.ExistsAsync();
            zipContainerExists.Should().BeFalse(
                "zip container should not be created when the note ID does not exist in the database");
        }
    }
}

