using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using FluentAssertions;
using HW4NoteKeeperEx1.Data;
using HW4NoteKeeperEx1.RequestAndResultObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace HW4NoteKeeperEx1.Tests
{
    /// <summary>
    /// Simple DTO for deserializing zip blob info from the GET all zip files endpoint.
    /// </summary>
    public class ZipBlobInfoResult
    {
        public string ZipFileId { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public DateTimeOffset CreatedDate { get; set; }
        public DateTimeOffset LastModifiedDate { get; set; }
        public long Length { get; set; }
    }

    /// <summary>
    /// End-to-end tests for the <c>NoteKeeperZipAttachmentController</c>.
    /// Covers POST (enqueue zip request), DELETE zip file, GET zip file by ID,
    /// GET all zip files, and the enhanced DELETE note-with-all-assets endpoint.
    /// </summary>
    /// <remarks>
    /// IMPORTANT: These tests run against live Azure services (deployed API + Azure Blob/Queue Storage)
    /// and must NOT run in parallel with other tests.
    /// Run only after the app has been published to Azure.
    /// </remarks>
    [Collection("Sequential")]
    [Trait("Category", "E2E")]
    public class NoteKeeperZipAttachmentE2ETests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
    {
        private static readonly string BaseUrl =
            "https://app-notekeeper-cscie94-ps-HW4-1-gjegduaqfccbd2bt.swedencentral-01.azurewebsites.net/";

        private const string StorageAccountName = "st4hw3";

        private readonly HttpClient _client;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly QueueServiceClient _queueServiceClient;
        private readonly JsonSerializerOptions _jsonOptions;

        // NoteIds created during tests – cleaned up in DisposeAsync
        private readonly List<string> _createdNoteIds = new();

        public NoteKeeperZipAttachmentE2ETests(WebApplicationFactory<Program> factory)
        {
            _client = new HttpClient { BaseAddress = new Uri(BaseUrl) };

            IConfiguration config = new ConfigurationBuilder()
                .AddUserSecrets<NoteKeeperZipAttachmentE2ETests>()
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
                credential);

            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        public Task InitializeAsync() => Task.CompletedTask;

        /// <summary>
        /// After each test, deletes any notes (and their containers) created during the test.
        /// </summary>
        public async Task DisposeAsync()
        {
            foreach (string noteId in _createdNoteIds)
            {
                // Use the enhanced DELETE to clean up note + all storage assets
                await _client.DeleteAsync($"notes/{noteId}");
            }
        }

        // ─── Helper: create a note via the API ───────────────────────────────────

        private async Task<string> CreateTestNoteAsync(string details = "Zip E2E test note")
        {
            var noteInput = new
            {
                details,
                tags = new[] { new { tagName = "zip-test" } }
            };

            HttpResponseMessage response = await _client.PostAsJsonAsync("NoteKeeper", noteInput);
            response.StatusCode.Should().Be(HttpStatusCode.Created);

            string body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            string noteId = doc.RootElement.GetProperty("id").GetString()!;
            _createdNoteIds.Add(noteId);
            return noteId;
        }

        /// <summary>Uploads a text attachment to a note so it has blobs to zip.</summary>
        private async Task UploadTextAttachmentAsync(string noteId, string attachmentName = "test.txt")
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("Hello zip test attachment");
            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
            HttpResponseMessage response = await _client.PutAsync(
                $"NoteKeeper/{noteId}/attachments/{attachmentName}", content);
            response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        }

        // ─── POST /notes/{noteId}/attachmentzipfiles ──────────────────────────────

        [Fact]
        public async Task RequestZipCreation_Returns202_WhenNoteHasAttachments()
        {
            string noteId = await CreateTestNoteAsync();
            await UploadTextAttachmentAsync(noteId);

            HttpResponseMessage response = await _client.PostAsync($"notes/{noteId}/attachmentzipfiles", null);

            response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        [Fact]
        public async Task RequestZipCreation_Returns204_WhenNoteHasNoAttachments()
        {
            string noteId = await CreateTestNoteAsync();

            HttpResponseMessage response = await _client.PostAsync($"notes/{noteId}/attachmentzipfiles", null);

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        [Fact]
        public async Task RequestZipCreation_Returns404_WhenNoteDoesNotExist()
        {
            string nonExistentNoteId = Guid.NewGuid().ToString();

            HttpResponseMessage response = await _client.PostAsync(
                $"notes/{nonExistentNoteId}/attachmentzipfiles", null);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task RequestZipCreation_Returns400_WhenNoteIdIsInvalidGuid()
        {
            HttpResponseMessage response = await _client.PostAsync(
                "notes/not-a-valid-guid/attachmentzipfiles", null);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task RequestZipCreation_ReturnsLocationHeader_WhenAccepted()
        {
            string noteId = await CreateTestNoteAsync();
            await UploadTextAttachmentAsync(noteId);

            HttpResponseMessage response = await _client.PostAsync($"notes/{noteId}/attachmentzipfiles", null);

            response.StatusCode.Should().Be(HttpStatusCode.Accepted);
            response.Headers.Location.Should().NotBeNull();
            response.Headers.Location!.ToString().Should().Contain("attachmentzipfiles");
        }

        // ─── GET /notes/{noteId}/attachmentzipfiles ───────────────────────────────

        [Fact]
        public async Task GetAllZipFiles_ReturnsEmptyArray_WhenNoZipsExist()
        {
            string noteId = await CreateTestNoteAsync();

            HttpResponseMessage response = await _client.GetAsync($"notes/{noteId}/attachmentzipfiles");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            string body = await response.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<List<ZipBlobInfoResult>>(body, _jsonOptions);
            items.Should().NotBeNull();
            items!.Should().BeEmpty();
        }

        [Fact]
        public async Task GetAllZipFiles_Returns404_WhenNoteDoesNotExist()
        {
            HttpResponseMessage response = await _client.GetAsync(
                $"notes/{Guid.NewGuid()}/attachmentzipfiles");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetAllZipFiles_Returns400_WhenNoteIdIsInvalidGuid()
        {
            HttpResponseMessage response = await _client.GetAsync(
                "notes/not-a-guid/attachmentzipfiles");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // ─── GET /notes/{noteId}/attachmentzipfiles/{zipFileId} ──────────────────

        [Fact]
        public async Task GetZipFile_Returns404_WhenNoteDoesNotExist()
        {
            HttpResponseMessage response = await _client.GetAsync(
                $"notes/{Guid.NewGuid()}/attachmentzipfiles/somefile.zip");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetZipFile_Returns404_WhenZipBlobDoesNotExist()
        {
            string noteId = await CreateTestNoteAsync();

            HttpResponseMessage response = await _client.GetAsync(
                $"notes/{noteId}/attachmentzipfiles/nonexistent-{Guid.NewGuid()}.zip");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetZipFile_Returns400_WhenNoteIdIsInvalidGuid()
        {
            HttpResponseMessage response = await _client.GetAsync(
                "notes/not-a-guid/attachmentzipfiles/file.zip");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // ─── DELETE /notes/{noteId}/attachmentzipfiles/{zipFileId} ────────────────

        [Fact]
        public async Task DeleteZipFile_Returns204_WhenZipBlobDoesNotExist()
        {
            string noteId = await CreateTestNoteAsync();

            // Deleting a non-existent zip blob is idempotent → 204
            HttpResponseMessage response = await _client.DeleteAsync(
                $"notes/{noteId}/attachmentzipfiles/{Guid.NewGuid()}.zip");

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        [Fact]
        public async Task DeleteZipFile_Returns404_WhenNoteDoesNotExist()
        {
            HttpResponseMessage response = await _client.DeleteAsync(
                $"notes/{Guid.NewGuid()}/attachmentzipfiles/file.zip");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task DeleteZipFile_Returns400_WhenNoteIdIsInvalidGuid()
        {
            HttpResponseMessage response = await _client.DeleteAsync(
                "notes/not-a-guid/attachmentzipfiles/file.zip");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // ─── DELETE /notes/{noteId} (enhanced) ───────────────────────────────────

        [Fact]
        public async Task DeleteNoteWithAllAssets_Returns204_WhenNoteExists()
        {
            string noteId = await CreateTestNoteAsync();
            _createdNoteIds.Remove(noteId); // We'll delete it manually in this test

            HttpResponseMessage response = await _client.DeleteAsync($"notes/{noteId}");

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        [Fact]
        public async Task DeleteNoteWithAllAssets_Returns404_WhenNoteDoesNotExist()
        {
            HttpResponseMessage response = await _client.DeleteAsync(
                $"notes/{Guid.NewGuid()}");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task DeleteNoteWithAllAssets_Returns400_WhenNoteIdIsInvalidGuid()
        {
            HttpResponseMessage response = await _client.DeleteAsync("notes/not-a-guid");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task DeleteNoteWithAllAssets_AlsoDeletesAttachmentBlobs()
        {
            string noteId = await CreateTestNoteAsync();
            await UploadTextAttachmentAsync(noteId, "file1.txt");
            _createdNoteIds.Remove(noteId);

            HttpResponseMessage deleteResponse = await _client.DeleteAsync($"notes/{noteId}");
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // The attachment container should no longer exist
            var containerClient = _blobServiceClient.GetBlobContainerClient(noteId);
            bool exists = await containerClient.ExistsAsync();
            exists.Should().BeFalse("attachment container should be deleted by the enhanced DELETE");
        }

        // ─── Full ZIP cycle test ──────────────────────────────────────────────────

        [Fact]
        public async Task FullZipCycle_RequestListDownloadDeleteZip_WorksCorrectly()
        {
            // ===== ARRANGE =====
            string noteId = await CreateTestNoteAsync("Full zip cycle test note");
            await UploadTextAttachmentAsync(noteId, "cycle-attachment.txt");

            // ===== POST – request zip creation =====
            HttpResponseMessage postResponse = await _client.PostAsync(
                $"notes/{noteId}/attachmentzipfiles", null);
            postResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

            // Extract the zipFileId from the Location header
            string locationUrl = postResponse.Headers.Location!.ToString();
            string zipFileId = locationUrl.Split('/').Last();
            zipFileId.Should().EndWith(".zip");

            // The Azure Function processes the queue asynchronously – wait for it
            await WaitForZipBlobAsync(noteId, zipFileId, maxWaitSeconds: 60);

            // ===== GET all zip files – should list our zip =====
            HttpResponseMessage listResponse = await _client.GetAsync(
                $"notes/{noteId}/attachmentzipfiles");
            listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var zipFiles = JsonSerializer.Deserialize<List<ZipBlobInfoResult>>(
                await listResponse.Content.ReadAsStringAsync(), _jsonOptions);
            zipFiles.Should().ContainSingle(z => z.ZipFileId == zipFileId);

            // ===== GET zip by ID – should download the zip =====
            HttpResponseMessage downloadResponse = await _client.GetAsync(
                $"notes/{noteId}/attachmentzipfiles/{zipFileId}");
            downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            downloadResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/zip");
            byte[] zipBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
            zipBytes.Length.Should().BeGreaterThan(0);

            // ===== DELETE zip file =====
            HttpResponseMessage deleteZipResponse = await _client.DeleteAsync(
                $"notes/{noteId}/attachmentzipfiles/{zipFileId}");
            deleteZipResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // ===== GET – zip should be gone =====
            HttpResponseMessage getAfterDelete = await _client.GetAsync(
                $"notes/{noteId}/attachmentzipfiles/{zipFileId}");
            getAfterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // ─── Helper: wait for zip blob to appear in storage ──────────────────────

        private async Task WaitForZipBlobAsync(string noteId, string zipFileId, int maxWaitSeconds = 60)
        {
            string zipContainerName = $"{noteId.ToLower()}-zip";
            var containerClient = _blobServiceClient.GetBlobContainerClient(zipContainerName);
            var blobClient = containerClient.GetBlobClient(zipFileId);

            DateTime deadline = DateTime.UtcNow.AddSeconds(maxWaitSeconds);
            while (DateTime.UtcNow < deadline)
            {
                if (await containerClient.ExistsAsync() && await blobClient.ExistsAsync())
                    return;

                await Task.Delay(3000); // poll every 3 seconds
            }

            throw new TimeoutException(
                $"Zip blob '{zipFileId}' did not appear in container '{zipContainerName}' within {maxWaitSeconds}s.");
        }
    }
}
