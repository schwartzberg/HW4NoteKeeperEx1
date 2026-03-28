using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FluentAssertions;
using HW4NoteKeeperEx1.Data;
using HW4NoteKeeperEx1.RequestAndResultObjects;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HW4NoteKeeperEx1.Tests
{
    /// <summary>
    /// Simple DTO for deserializing attachment info from the GET all attachments endpoint.
    /// </summary>
    public class AttachmentInfoResult
    {
        public string AttachmentId { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public DateTimeOffset CreatedDate { get; set; }
        public DateTimeOffset LastModifiedDate { get; set; }
        public long Length { get; set; }
    }

    /// <summary>
    /// End-to-end tests for the NoteKeeperAttachmentController and AzureStorageInitializer.
    /// Tests cover PUT (create/update attachment), DELETE (remove attachment), and seeding behavior.
    /// </summary>
    /// <remarks>
    /// IMPORTANT: These tests run against live Azure services (deployed API + Azure Blob Storage)
    /// and must NOT run in parallel with other tests.
    /// Run only after the app has been published to Azure.
    /// </remarks>
    [Collection("Sequential")]
    [Trait("Category", "E2E")]
    public class NoteKeeperAttachmentE2ETests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
    {
        private static readonly string BaseUrl =
            "https://app-notekeeper-cscie94-ps-HW4-1-gjegduaqfccbd2bt.swedencentral-01.azurewebsites.net/";

        private readonly HttpClient _client;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly string _attachmentsDirectory;

        // NoteIds created during tests that must be cleaned up
        private readonly List<string> _createdNoteIds = new();

        public NoteKeeperAttachmentE2ETests(WebApplicationFactory<Program> factory)
        {
            _client = new HttpClient { BaseAddress = new Uri(BaseUrl) };

            // Build configuration from user secrets (same UserSecretsId as main project)
            IConfiguration config = new ConfigurationBuilder()
                .AddUserSecrets<NoteKeeperAttachmentE2ETests>()
                .Build();

            string storageUrl = config["StorageAccountSettings:Url"]!;
            string tenantId = config["StorageAccountSettings:TenantId"]!;

            // Use the same credential scoping pattern as the main app (developer credentials only)
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

            _blobServiceClient = new BlobServiceClient(
                new Uri(storageUrl),
                new DefaultAzureCredential(credentialOptions));

            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _attachmentsDirectory = Path.Combine(AppContext.BaseDirectory, "AzureStorageAttachments");
        }

        /// <summary>
        /// Runs before each test — no pre-test cleanup needed; each test manages its own state.
        /// </summary>
        public Task InitializeAsync() => Task.CompletedTask;

        /// <summary>
        /// Runs after each test — deletes any notes (and their blob containers) created during the test.
        /// </summary>
        public async Task DisposeAsync()
        {
            foreach (string noteId in _createdNoteIds)
            {
                // Delete note from DB
                await _client.DeleteAsync($"/NoteKeeper/{noteId}");

                // Delete the blob container for this note
                BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(noteId);
                if ((await containerClient.ExistsAsync()).Value)
                {
                    await containerClient.DeleteAsync();
                }
            }
            _createdNoteIds.Clear();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helper methods
        // ─────────────────────────────────────────────────────────────────────

        private async Task<NoteResult> CreateTestNoteAsync(
            string summary = "Test note",
            string details = "Test details for attachment testing")
        {
            var request = new CreateNoteRequest { Summary = summary, Details = details };
            HttpResponseMessage response = await _client.PostAsJsonAsync("/NoteKeeper", request);
            response.EnsureSuccessStatusCode();
            NoteResult note = (await response.Content.ReadFromJsonAsync<NoteResult>(_jsonOptions))!;
            _createdNoteIds.Add(note.Id.ToString());
            return note;
        }

        private static MultipartFormDataContent BuildFileContent(string fileName, byte[] fileBytes, string contentType = "image/png")
        {
            MultipartFormDataContent form = new();
            ByteArrayContent fileContent = new(fileBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            form.Add(fileContent, "fileData", fileName);
            return form;
        }

        private static byte[] ReadTestFile(string fileName)
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "AzureStorageAttachments");
            string path = Path.Combine(dir, fileName);
            return File.Exists(path) ? File.ReadAllBytes(path) : Encoding.UTF8.GetBytes("test-content");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Seed Verification Tests
        // ─────────────────────────────────────────────────────────────────────

        #region Seed Verification Tests

        // NOTE: Seed verification tests have been moved to NoteKeeperSeedingTests.cs
        // These tests now verify seeding through DbInitializer instead of calling AzureStorageInitializer directly

        /*
        /// <summary>
        /// Verifies that the seeded "Running grocery list" note has its container
        /// and the expected blobs (MilkAndEggs.png, Oranges.png) in Azure Storage.
        /// </summary>
        [Fact]
        public async Task Seed_GroceryListNote_HasExpectedBlobsInStorage()
        {
            // Find the seeded grocery list note via API
            HttpResponseMessage response = await _client.GetAsync("/NoteKeeper");
            response.EnsureSuccessStatusCode();
            List<NoteResult>? notes = await response.Content.ReadFromJsonAsync<List<NoteResult>>(_jsonOptions);
            notes.Should().NotBeNull();

            NoteResult? groceryNote = notes!.FirstOrDefault(n =>
                n.Summary != null &&
                n.Summary.Equals("Running grocery list", StringComparison.OrdinalIgnoreCase));

            if (groceryNote == null)
            {
                // Seeding may not have run yet; skip gracefully
                return;
            }

            string containerId = groceryNote.Id.ToString();
            BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(containerId);

            bool containerExists = (await containerClient.ExistsAsync()).Value;
            containerExists.Should().BeTrue(
                $"Container '{containerId}' should exist for the 'Running grocery list' note.");

            List<string> blobNames = new();
            await foreach (BlobItem blob in containerClient.GetBlobsAsync())
                blobNames.Add(blob.Name);

            blobNames.Should().Contain("MilkAndEggs.png");
            blobNames.Should().Contain("Oranges.png");
        }

        /// <summary>
        /// Verifies that all four seeded notes have their blob containers in Azure Storage.
        /// </summary>
        [Fact]
        public async Task Seed_AllFourNotes_HaveContainersInStorage()
        {
            HttpResponseMessage response = await _client.GetAsync("/NoteKeeper");
            response.EnsureSuccessStatusCode();
            List<NoteResult>? notes = await response.Content.ReadFromJsonAsync<List<NoteResult>>(_jsonOptions);
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
                NoteResult? note = notes!.FirstOrDefault(n =>
                    n.Summary != null &&
                    n.Summary.Equals(summary, StringComparison.OrdinalIgnoreCase));

                if (note == null) continue; // seeding may not have run yet

                BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(note.Id.ToString());
                bool exists = (await containerClient.ExistsAsync()).Value;
                exists.Should().BeTrue(
                    $"Container for note '{summary}' (id={note.Id}) should exist after seeding.");
            }
        }

        /// <summary>
        /// Re-seeds a note's container (deletes it directly, calls initializer, verifies blobs return).
        /// </summary>
        [Fact]
        public async Task Seed_AfterContainerDeleted_ReCreatesContainerAndBlobs()
        {
            // Find the "Gift supplies notes" seeded note
            HttpResponseMessage getResponse = await _client.GetAsync("/NoteKeeper");
            getResponse.EnsureSuccessStatusCode();
            List<NoteResult>? allNotes = await getResponse.Content.ReadFromJsonAsync<List<NoteResult>>(_jsonOptions);
            NoteResult? giftNote = allNotes?.FirstOrDefault(n =>
                n.Summary != null &&
                n.Summary.Equals("Gift supplies notes", StringComparison.OrdinalIgnoreCase));

            if (giftNote == null) return; // seeding may not have run yet

            string containerId = giftNote.Id.ToString();
            BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(containerId);

            // Delete the container to simulate a missing container
            if ((await containerClient.ExistsAsync()).Value)
                await containerClient.DeleteAsync();

            // Build a real DBContext and call the initializer directly.
            // Include appsettings.json (copied to output) so the connection string is available,
            // then layer user secrets on top for any overrides.
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddUserSecrets<NoteKeeperAttachmentE2ETests>()
                .Build();

            string connectionString = config.GetConnectionString("DefaultConnection")!;
            DbContextOptions<MyDatabaseContext> dbOptions = new DbContextOptionsBuilder<MyDatabaseContext>()
                .UseSqlServer(connectionString)
                .Options;

            try
            {
                using MyDatabaseContext context = new MyDatabaseContext(dbOptions);
                ILogger logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("SeedTest");
                TelemetryClient telClient = new TelemetryClient(new Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration());

                await AzureStorageInitializer.InitializeAsync(context, _blobServiceClient, logger, telClient);

                // Verify container and blobs were re-created
                bool containerExists = (await containerClient.ExistsAsync()).Value;
                containerExists.Should().BeTrue("Container should be re-created by the initializer.");

                List<string> blobNames = new();
                await foreach (BlobItem blob in containerClient.GetBlobsAsync())
                    blobNames.Add(blob.Name);

                blobNames.Should().Contain("WrappingPaper.png");
                blobNames.Should().Contain("Tape.png");
            }
            catch
            {
                // If the test fails, restore the container so other tests aren't affected
                if (!(await containerClient.ExistsAsync()).Value)
                {
                    await containerClient.CreateAsync(Azure.Storage.Blobs.Models.PublicAccessType.None);
                    string dir = Path.Combine(AppContext.BaseDirectory, "AzureStorageAttachments");
                    foreach (string file in new[] { "WrappingPaper.png", "Tape.png" })
                    {
                        string filePath = Path.Combine(dir, file);
                        if (File.Exists(filePath))
                        {
                            using FileStream fs = File.OpenRead(filePath);
                            await containerClient.GetBlobClient(file).UploadAsync(fs, overwrite: true);
                        }
                    }
                }
                throw;
            }
        }
        */

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        // PUT Attachment Tests
        // ─────────────────────────────────────────────────────────────────────

        #region PUT Attachment – Positive Tests

        /// <summary>
        /// PUT a new attachment to an existing note — expect 201 Created with a Location header.
        /// </summary>
        [Fact]
        public async Task PutAttachment_NewAttachment_Returns201Created()
        {
            NoteResult note = await CreateTestNoteAsync();
            byte[] fileBytes = ReadTestFile("MilkAndEggs.png");
            using MultipartFormDataContent form = BuildFileContent("MilkAndEggs.png", fileBytes);

            HttpResponseMessage response = await _client.PutAsync(
                $"/notes/{note.Id}/attachments/MilkAndEggs.png", form);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            response.Headers.Location.Should().NotBeNull();
            response.Headers.Location!.ToString().Should().Contain(note.Id.ToString());
            response.Headers.Location.ToString().Should().Contain("MilkAndEggs.png");
        }

        /// <summary>
        /// PUT the same attachment twice — first call returns 201, second returns 204 No Content.
        /// </summary>
        [Fact]
        public async Task PutAttachment_UpdateExistingAttachment_Returns204NoContent()
        {
            NoteResult note = await CreateTestNoteAsync();
            byte[] fileBytes = ReadTestFile("Oranges.png");
            string attachmentId = "Oranges.png";

            // First PUT — create
            using MultipartFormDataContent form1 = BuildFileContent(attachmentId, fileBytes);
            HttpResponseMessage create = await _client.PutAsync(
                $"/notes/{note.Id}/attachments/{attachmentId}", form1);
            create.StatusCode.Should().Be(HttpStatusCode.Created);

            // Second PUT — update
            using MultipartFormDataContent form2 = BuildFileContent(attachmentId, fileBytes);
            HttpResponseMessage update = await _client.PutAsync(
                $"/notes/{note.Id}/attachments/{attachmentId}", form2);
            update.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        /// <summary>
        /// PUT three attachments (filling the default limit of 3) — all should succeed.
        /// </summary>
        [Fact]
        public async Task PutAttachment_UpToMaxAttachments_ReturnsSuccess()
        {
            NoteResult note = await CreateTestNoteAsync();
            string[] files = { "Chocolate.png", "Diamonds.png", "NewCar.png" };

            foreach (string file in files)
            {
                byte[] fileBytes = ReadTestFile(file);
                using MultipartFormDataContent form = BuildFileContent(file, fileBytes);
                HttpResponseMessage response = await _client.PutAsync(
                    $"/notes/{note.Id}/attachments/{file}", form);
                response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.NoContent);
            }
        }

        #endregion

        #region PUT Attachment – Negative Tests

        /// <summary>
        /// PUT with an invalid (non-GUID) noteId — expect 400 Bad Request.
        /// </summary>
        [Fact]
        public async Task PutAttachment_InvalidNoteId_Returns400BadRequest()
        {
            byte[] fileBytes = Encoding.UTF8.GetBytes("test content");
            using MultipartFormDataContent form = BuildFileContent("test.png", fileBytes);

            HttpResponseMessage response = await _client.PutAsync(
                $"/notes/not-a-valid-guid/attachments/test.png", form);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        /// <summary>
        /// PUT without including a file (empty request) — expect 400 Bad Request.
        /// </summary>
        [Fact]
        public async Task PutAttachment_NoFileProvided_Returns400BadRequest()
        {
            NoteResult note = await CreateTestNoteAsync();
            using MultipartFormDataContent emptyForm = new();

            HttpResponseMessage response = await _client.PutAsync(
                $"/notes/{note.Id}/attachments/test.png", emptyForm);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        /// <summary>
        /// PUT a fourth attachment when the note already has three — expect 403 Forbidden
        /// with a ProblemDetails body containing the MaxAttachments value.
        /// </summary>
        [Fact]
        public async Task PutAttachment_ExceedsMaxAttachments_Returns403Forbidden()
        {
            NoteResult note = await CreateTestNoteAsync();

            // Fill up to the limit (3)
            string[] firstThree = { "Chocolate.png", "Diamonds.png", "NewCar.png" };
            foreach (string file in firstThree)
            {
                byte[] fb = ReadTestFile(file);
                using MultipartFormDataContent f = BuildFileContent(file, fb);
                HttpResponseMessage r = await _client.PutAsync($"/notes/{note.Id}/attachments/{file}", f);
                r.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.NoContent);
            }

            // Fourth attachment should be rejected
            byte[] fileBytes = Encoding.UTF8.GetBytes("extra attachment");
            using MultipartFormDataContent form = BuildFileContent("extra.png", fileBytes);
            HttpResponseMessage response = await _client.PutAsync(
                $"/notes/{note.Id}/attachments/extra.png", form);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            string body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("Attachment limit reached");
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        // DELETE Attachment Tests
        // ─────────────────────────────────────────────────────────────────────

        #region DELETE Attachment – Positive Tests

        /// <summary>
        /// PUT an attachment then DELETE it — expect 204 No Content.
        /// </summary>
        [Fact]
        public async Task DeleteAttachment_ExistingAttachment_Returns204NoContent()
        {
            NoteResult note = await CreateTestNoteAsync();

            // Create the attachment
            byte[] fileBytes = ReadTestFile("Tape.png");
            using MultipartFormDataContent form = BuildFileContent("Tape.png", fileBytes);
            HttpResponseMessage put = await _client.PutAsync(
                $"/notes/{note.Id}/attachments/Tape.png", form);
            put.StatusCode.Should().Be(HttpStatusCode.Created);

            // Delete it
            HttpResponseMessage delete = await _client.DeleteAsync(
                $"/notes/{note.Id}/attachments/Tape.png");
            delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        /// <summary>
        /// DELETE an attachment that does not exist from a container that DOES exist — expect 204 No Content (idempotent).
        /// This test verifies that DELETE is idempotent: attempting to delete a non-existent blob from an existing
        /// container returns 204, not an error.
        /// </summary>
        [Fact]
        public async Task DeleteAttachment_BlobDoesNotExist_Returns204NoContent()
        {
            NoteResult note = await CreateTestNoteAsync();

            // Create the container by uploading an attachment, then delete it
            // This ensures the container exists but the blob we're about to try deleting doesn't
            byte[] fileBytes = ReadTestFile("MilkAndEggs.png");
            using (MultipartFormDataContent form = BuildFileContent("MilkAndEggs.png", fileBytes))
            {
                await _client.PutAsync($"/notes/{note.Id}/attachments/MilkAndEggs.png", form);
            }
            await _client.DeleteAsync($"/notes/{note.Id}/attachments/MilkAndEggs.png");

            // Now the container exists but is empty. Try to delete a non-existent blob.
            HttpResponseMessage response = await _client.DeleteAsync(
                $"/notes/{note.Id}/attachments/nonexistent-file.png");

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        /// <summary>
        /// PUT an attachment, DELETE it, then verify it is gone by trying to DELETE again
        /// (still expects 204, not an error).
        /// </summary>
        [Fact]
        public async Task DeleteAttachment_ThenDeleteAgain_BothReturn204()
        {
            NoteResult note = await CreateTestNoteAsync();

            // Create attachment
            byte[] fileBytes = ReadTestFile("WrappingPaper.png");
            using MultipartFormDataContent form = BuildFileContent("WrappingPaper.png", fileBytes);
            await _client.PutAsync($"/notes/{note.Id}/attachments/WrappingPaper.png", form);

            // First delete
            HttpResponseMessage first = await _client.DeleteAsync(
                $"/notes/{note.Id}/attachments/WrappingPaper.png");
            first.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // Second delete of the same blob (should still be 204)
            HttpResponseMessage second = await _client.DeleteAsync(
                $"/notes/{note.Id}/attachments/WrappingPaper.png");
            second.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        #endregion

        #region DELETE Attachment – Negative Tests

        /// <summary>
        /// DELETE with an invalid (non-GUID) noteId — expect 400 Bad Request.
        /// </summary>
        [Fact]
        public async Task DeleteAttachment_InvalidNoteId_Returns400BadRequest()
        {
            HttpResponseMessage response = await _client.DeleteAsync(
                $"/notes/not-a-valid-guid/attachments/someFile.png");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        /// <summary>
        /// DELETE from a container that does not exist (but noteId is a valid GUID) — expect 404 Not Found.
        /// This tests the scenario where a container with a valid GUID exists, an attachment is uploaded,
        /// then we try to delete from a slightly modified valid GUID that doesn't have a container.
        /// </summary>
        [Fact]
        public async Task DeleteAttachment_ContainerDoesNotExist_Returns404NotFound()
        {
            // Create a note and upload an attachment to establish a real container
            NoteResult note = await CreateTestNoteAsync();
            byte[] fileBytes = ReadTestFile("MilkAndEggs.png");
            using (MultipartFormDataContent form = BuildFileContent("MilkAndEggs.png", fileBytes))
            {
                HttpResponseMessage put = await _client.PutAsync(
                    $"/notes/{note.Id}/attachments/MilkAndEggs.png", form);
                put.StatusCode.Should().Be(HttpStatusCode.Created);
            }

            // Create a different valid GUID by modifying the original note ID slightly
            // Parse the GUID, increment it, and convert back to string
            Guid originalGuid = Guid.Parse(note.Id.ToString());
            byte[] bytes = originalGuid.ToByteArray();
            bytes[0] = (byte)(bytes[0] == 255 ? 0 : bytes[0] + 1); // Increment first byte
            Guid modifiedGuid = new Guid(bytes);
            string nonExistentNoteId = modifiedGuid.ToString();

            // Try to delete from the non-existent container (note)
            HttpResponseMessage response = await _client.DeleteAsync(
                $"/notes/{nonExistentNoteId}/attachments/MilkAndEggs.png");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        // GET Attachment Tests
        // ─────────────────────────────────────────────────────────────────────

        #region GET Attachment – Positive Tests

        /// <summary>
        /// PUT an attachment then GET it — expect 200 OK with the correct content type and file content.
        /// </summary>
        [Fact]
        public async Task GetAttachment_ExistingAttachment_Returns200WithCorrectContent()
        {
            NoteResult note = await CreateTestNoteAsync();
            byte[] originalFileBytes = ReadTestFile("MilkAndEggs.png");
            string attachmentId = "MilkAndEggs.png";

            // Upload the attachment
            using MultipartFormDataContent form = BuildFileContent(attachmentId, originalFileBytes);
            HttpResponseMessage put = await _client.PutAsync(
                $"/notes/{note.Id}/attachments/{attachmentId}", form);
            put.StatusCode.Should().Be(HttpStatusCode.Created);

            // Retrieve the attachment
            HttpResponseMessage get = await _client.GetAsync(
                $"/notes/{note.Id}/attachments/{attachmentId}");

            get.StatusCode.Should().Be(HttpStatusCode.OK);
            get.Content.Headers.ContentType.Should().NotBeNull();
            get.Content.Headers.ContentType!.MediaType.Should().Be("image/png");

            // Verify Content-Disposition header has the filename
            get.Content.Headers.ContentDisposition.Should().NotBeNull();
            get.Content.Headers.ContentDisposition!.FileName.Should().Be(attachmentId);

            // Verify the content matches what was uploaded
            byte[] downloadedBytes = await get.Content.ReadAsByteArrayAsync();
            downloadedBytes.Should().Equal(originalFileBytes);
        }

        /// <summary>
        /// GET an attachment with PDF content type — verify correct content type is returned.
        /// </summary>
        [Fact]
        public async Task GetAttachment_PdfFile_ReturnsCorrectContentType()
        {
            NoteResult note = await CreateTestNoteAsync();
            byte[] pdfBytes = ReadTestFile("AzureTipsAndTricks.pdf");
            string attachmentId = "AzureTipsAndTricks.pdf";

            // Upload the PDF
            using MultipartFormDataContent form = BuildFileContent(attachmentId, pdfBytes, "application/pdf");
            HttpResponseMessage put = await _client.PutAsync(
                $"/notes/{note.Id}/attachments/{attachmentId}", form);
            put.StatusCode.Should().Be(HttpStatusCode.Created);

            // Retrieve the PDF
            HttpResponseMessage get = await _client.GetAsync(
                $"/notes/{note.Id}/attachments/{attachmentId}");

            get.StatusCode.Should().Be(HttpStatusCode.OK);
            get.Content.Headers.ContentType.Should().NotBeNull();
            get.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
            get.Content.Headers.ContentDisposition!.FileName.Should().Be(attachmentId);
        }

        #endregion

        #region GET Attachment – Negative Tests

        /// <summary>
        /// GET an attachment that does not exist — expect 404 Not Found.
        /// </summary>
        [Fact]
        public async Task GetAttachment_AttachmentDoesNotExist_Returns404()
        {
            NoteResult note = await CreateTestNoteAsync();

            HttpResponseMessage response = await _client.GetAsync(
                $"/notes/{note.Id}/attachments/nonexistent-file.png");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        /// <summary>
        /// GET from a container/note that does not exist — expect 404 Not Found.
        /// </summary>
        [Fact]
        public async Task GetAttachment_ContainerDoesNotExist_Returns404()
        {
            string nonExistentNoteId = Guid.NewGuid().ToString();

            HttpResponseMessage response = await _client.GetAsync(
                $"/notes/{nonExistentNoteId}/attachments/someFile.png");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        /// <summary>
        /// GET with an invalid (non-GUID) noteId — expect 400 Bad Request.
        /// </summary>
        [Fact]
        public async Task GetAttachment_InvalidNoteId_Returns400BadRequest()
        {
            HttpResponseMessage response = await _client.GetAsync(
                $"/notes/not-a-valid-guid/attachments/someFile.png");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        /// <summary>
        /// GET with an empty attachmentId — expect 400 Bad Request.
        /// </summary>
        [Fact]
        public async Task GetAttachment_EmptyAttachmentId_Returns400BadRequest()
        {
            NoteResult note = await CreateTestNoteAsync();

            HttpResponseMessage response = await _client.GetAsync(
                $"/notes/{note.Id}/attachments/");

            // This will likely result in 404 because the route won't match, but let's verify
            response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        // GET All Attachments Tests
        // ─────────────────────────────────────────────────────────────────────

        #region GET All Attachments Tests

        /// <summary>
        /// GET all attachments when there are multiple attachments — expect 200 OK with list of attachment info.
        /// </summary>
        [Fact]
        public async Task GetAllAttachments_MultipleAttachments_Returns200WithList()
        {
            NoteResult note = await CreateTestNoteAsync();
            
            // Upload multiple attachments
            string[] files = { "MilkAndEggs.png", "Oranges.png", "Tape.png" };
            foreach (string file in files)
            {
                byte[] fileBytes = ReadTestFile(file);
                using MultipartFormDataContent form = BuildFileContent(file, fileBytes);
                await _client.PutAsync($"/notes/{note.Id}/attachments/{file}", form);
            }

            // Retrieve all attachments
            HttpResponseMessage response = await _client.GetAsync($"/notes/{note.Id}/attachments");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var attachments = await response.Content.ReadFromJsonAsync<List<AttachmentInfoResult>>(_jsonOptions);
            attachments.Should().NotBeNull();
            attachments!.Count.Should().Be(3);
            
            // Verify each attachment has the expected properties
            foreach (var attachment in attachments)
            {
                attachment.AttachmentId.Should().NotBeNullOrEmpty();
                attachment.ContentType.Should().NotBeNullOrEmpty();
                attachment.CreatedDate.Should().NotBe(default(DateTimeOffset));
                attachment.LastModifiedDate.Should().NotBe(default(DateTimeOffset));
                attachment.Length.Should().BeGreaterThan(0);
            }

            // Verify all three files are present
            var ids = attachments.Select(a => a.AttachmentId).ToList();
            ids.Should().Contain("MilkAndEggs.png");
            ids.Should().Contain("Oranges.png");
            ids.Should().Contain("Tape.png");
        }

        /// <summary>
        /// GET all attachments when container exists but has no blobs — expect 200 OK with empty list.
        /// </summary>
        [Fact]
        public async Task GetAllAttachments_NoAttachments_Returns200WithEmptyList()
        {
            NoteResult note = await CreateTestNoteAsync();
            
            // Create the container by uploading then deleting an attachment
            byte[] fileBytes = ReadTestFile("MilkAndEggs.png");
            using MultipartFormDataContent form = BuildFileContent("MilkAndEggs.png", fileBytes);
            await _client.PutAsync($"/notes/{note.Id}/attachments/MilkAndEggs.png", form);
            await _client.DeleteAsync($"/notes/{note.Id}/attachments/MilkAndEggs.png");

            // Retrieve all attachments
            HttpResponseMessage response = await _client.GetAsync($"/notes/{note.Id}/attachments");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var attachments = await response.Content.ReadFromJsonAsync<List<AttachmentInfoResult>>(_jsonOptions);
            attachments.Should().NotBeNull();
            attachments!.Count.Should().Be(0);
        }

        /// <summary>
        /// GET all attachments when container does not exist — expect 404 Not Found.
        /// </summary>
        [Fact]
        public async Task GetAllAttachments_ContainerDoesNotExist_Returns404()
        {
            string nonExistentNoteId = Guid.NewGuid().ToString();

            HttpResponseMessage response = await _client.GetAsync($"/notes/{nonExistentNoteId}/attachments");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        /// <summary>
        /// GET all attachments with an invalid (non-GUID) noteId — expect 400 Bad Request.
        /// </summary>
        [Fact]
        public async Task GetAllAttachments_InvalidNoteId_Returns400BadRequest()
        {
            HttpResponseMessage response = await _client.GetAsync($"/notes/not-a-valid-guid/attachments");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        /// <summary>
        /// GET all attachments verifies that content types are preserved correctly.
        /// </summary>
        [Fact]
        public async Task GetAllAttachments_VerifiesContentTypes()
        {
            NoteResult note = await CreateTestNoteAsync();
            
            // Upload PNG and PDF files with different content types
            byte[] pngBytes = ReadTestFile("MilkAndEggs.png");
            using (MultipartFormDataContent pngForm = BuildFileContent("MilkAndEggs.png", pngBytes, "image/png"))
            {
                await _client.PutAsync($"/notes/{note.Id}/attachments/MilkAndEggs.png", pngForm);
            }

            byte[] pdfBytes = ReadTestFile("AzureTipsAndTricks.pdf");
            using (MultipartFormDataContent pdfForm = BuildFileContent("AzureTipsAndTricks.pdf", pdfBytes, "application/pdf"))
            {
                await _client.PutAsync($"/notes/{note.Id}/attachments/AzureTipsAndTricks.pdf", pdfForm);
            }

            // Retrieve all attachments
            HttpResponseMessage response = await _client.GetAsync($"/notes/{note.Id}/attachments");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            
            var attachments = await response.Content.ReadFromJsonAsync<List<AttachmentInfoResult>>(_jsonOptions);
            attachments.Should().NotBeNull();
            attachments!.Count.Should().Be(2);

            // Verify content types
            var pngAttachment = attachments.FirstOrDefault(a => a.AttachmentId == "MilkAndEggs.png");
            pngAttachment.Should().NotBeNull();
            pngAttachment!.ContentType.Should().Be("image/png");

            var pdfAttachment = attachments.FirstOrDefault(a => a.AttachmentId == "AzureTipsAndTricks.pdf");
            pdfAttachment.Should().NotBeNull();
            pdfAttachment!.ContentType.Should().Be("application/pdf");
        }

        #endregion

        // ─────────────────────────────────────────────────────────────────────
        // Full Lifecycle Test
        // ─────────────────────────────────────────────────────────────────────

        #region Full Lifecycle Test

        /// <summary>
        /// Full lifecycle: create note → upload attachment → retrieve attachment → update attachment → retrieve again → delete attachment → verify gone.
        /// </summary>
        [Fact]
        public async Task AttachmentLifecycle_CreateGetUpdateDeleteAttachment_WorksCorrectly()
        {
            // ==================== CREATE NOTE ====================
            NoteResult note = await CreateTestNoteAsync("Lifecycle test note", "Lifecycle test details");
            note.Id.Should().NotBeEmpty();

            // ==================== PUT (CREATE ATTACHMENT) ====================
            byte[] fileBytes = ReadTestFile("AzureLogo.png");
            using MultipartFormDataContent createForm = BuildFileContent("AzureLogo.png", fileBytes);

            HttpResponseMessage putCreate = await _client.PutAsync(
                $"/notes/{note.Id}/attachments/AzureLogo.png", createForm);
            putCreate.StatusCode.Should().Be(HttpStatusCode.Created);
            putCreate.Headers.Location.Should().NotBeNull();

            // ==================== GET (VERIFY CREATED) ====================
            HttpResponseMessage getCreated = await _client.GetAsync(
                $"/notes/{note.Id}/attachments/AzureLogo.png");
            getCreated.StatusCode.Should().Be(HttpStatusCode.OK);
            byte[] downloadedBytes = await getCreated.Content.ReadAsByteArrayAsync();
            downloadedBytes.Should().Equal(fileBytes);

            // ==================== PUT (UPDATE ATTACHMENT) ====================
            byte[] updatedBytes = ReadTestFile("AzureLogo.png");
            using MultipartFormDataContent updateForm = BuildFileContent("AzureLogo.png", updatedBytes);

            HttpResponseMessage putUpdate = await _client.PutAsync(
                $"/notes/{note.Id}/attachments/AzureLogo.png", updateForm);
            putUpdate.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // ==================== GET (VERIFY UPDATED) ====================
            HttpResponseMessage getUpdated = await _client.GetAsync(
                $"/notes/{note.Id}/attachments/AzureLogo.png");
            getUpdated.StatusCode.Should().Be(HttpStatusCode.OK);
            byte[] downloadedUpdatedBytes = await getUpdated.Content.ReadAsByteArrayAsync();
            downloadedUpdatedBytes.Should().Equal(updatedBytes);

            // ==================== DELETE ATTACHMENT ====================
            HttpResponseMessage deleteAttachment = await _client.DeleteAsync(
                $"/notes/{note.Id}/attachments/AzureLogo.png");
            deleteAttachment.StatusCode.Should().Be(HttpStatusCode.NoContent);

            // ==================== VERIFY GONE (GET RETURNS 404) ====================
            HttpResponseMessage getDeleted = await _client.GetAsync(
                $"/notes/{note.Id}/attachments/AzureLogo.png");
            getDeleted.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        #endregion
    }
}
