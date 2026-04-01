using Azure.Data.Tables;
using Azure.Identity;
using FluentAssertions;
using HW4NoteKeeperEx1.RequestAndResultObjects;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace HW4NoteKeeperEx1.Tests
{
    /// <summary>
    /// E2E tests for <c>NoteKeeperZipAttachmentControllerEx1</c>.
    /// Hits the live Ex1 app service and directly reads the Jobs Azure Table
    /// to verify that <c>InsertQueuedJobAsync</c> actually writes rows.
    /// 
    /// IMPORTANT: Publish the Ex1 app to Azure before running these tests.
    /// </summary>
    [Collection("Sequential")]
    [Trait("Category", "E2E")]
    public class NoteKeeperZipAttachmentControllerEx1E2ETests : IAsyncLifetime
    {
        // Ex1 app service URL
        private static readonly string BaseUrl =
            "https://app-notekeeper-cscie94-ps-hw4-ex1-fzb7ffggbqhhcybz.swedencentral-01.azurewebsites.net/";

        private const string StorageAccountName = "st4hw3";
        private const string TableUri = "https://st4hw3.table.core.windows.net";
        private const string JobsTableName = "Jobs";

        private readonly HttpClient _client;
        private readonly TableClient _tableClient;
        private readonly JsonSerializerOptions _jsonOptions;

        // Track noteIds and job rows for cleanup
        private readonly List<string> _createdNoteIds = new();
        private readonly List<(string PartitionKey, string RowKey)> _createdJobRows = new();

        public NoteKeeperZipAttachmentControllerEx1E2ETests()
        {
            _client = new HttpClient { BaseAddress = new Uri(BaseUrl) };

            IConfiguration config = new ConfigurationBuilder()
                .AddUserSecrets<NoteKeeperZipAttachmentControllerEx1E2ETests>()
                .Build();

            string tenantId = config["StorageAccountSettings:TenantId"]
                ?? "d607a394-7dc0-4c7d-8b9e-a9ed69c728b9";

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

            var tableServiceClient = new TableServiceClient(new Uri(TableUri), credential);
            _tableClient = tableServiceClient.GetTableClient(JobsTableName);

            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            // Clean up job rows we created
            foreach (var (pk, rk) in _createdJobRows)
            {
                try { await _tableClient.DeleteEntityAsync(pk, rk); } catch { }
            }

            // Clean up notes we created
            foreach (string noteId in _createdNoteIds)
            {
                try { await _client.DeleteAsync($"notes/{noteId}"); } catch { }
            }
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────────

        private async Task<string> CreateTestNoteAsync(string details = "Ex1 E2E test note")
        {
            var noteInput = new
            {
                details,
                tags = new[] { new { tagName = "ex1-e2e-test" } }
            };

            HttpResponseMessage response = await _client.PostAsJsonAsync("NoteKeeper", noteInput);
            response.StatusCode.Should().Be(HttpStatusCode.Created,
                "creating a test note should succeed");

            string body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            string noteId = doc.RootElement.GetProperty("id").GetString()!;
            _createdNoteIds.Add(noteId);
            return noteId;
        }

        private async Task UploadTextAttachmentAsync(string noteId, string name = "test.txt")
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("Ex1 E2E attachment content");
            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
            HttpResponseMessage response = await _client.PutAsync(
                $"NoteKeeper/{noteId}/attachments/{name}", content);
            response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        }

        /// <summary>
        /// Extracts the zipFileId from the Location header returned by the Ex1 POST.
        /// Location format: https://host/notes/{noteId}/attachmentzipfiles/jobs/{zipFileId}
        /// </summary>
        private static string ExtractZipFileIdFromLocation(Uri location)
        {
            string path = location.AbsolutePath; // /notes/{noteId}/attachmentzipfiles/jobs/{zipFileId}
            return path.Split('/').Last();        // {zipFileId}
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        //  POST /notes/{noteId}/attachmentzipfilesex1 — verify Jobs table row
        // ═══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// THE KEY TEST: POST to the Ex1 endpoint, then IMMEDIATELY read the Jobs table
        /// to verify a Queued row was inserted. This is the test to debug the empty Jobs table bug.
        /// Set a breakpoint on JobsTableService.InsertQueuedJobAsync line 48 and debug this test.
        /// </summary>
        [Fact]
        public async Task PostEx1_InsertsQueuedRow_InJobsTable()
        {
            // Arrange — create a note with an attachment
            string noteId = await CreateTestNoteAsync("PostEx1 Jobs table verification");
            await UploadTextAttachmentAsync(noteId);

            // Act — POST to the Ex1 endpoint
            HttpResponseMessage postResponse = await _client.PostAsync(
                $"notes/{noteId}/attachmentzipfilesex1", null);

            postResponse.StatusCode.Should().Be(HttpStatusCode.Accepted,
                "Ex1 POST should return 202 Accepted");
            postResponse.Headers.Location.Should().NotBeNull(
                "Ex1 POST should return a Location header");

            string zipFileId = ExtractZipFileIdFromLocation(postResponse.Headers.Location!);
            zipFileId.Should().EndWith(".zip");

            // Track for cleanup
            string normalizedNoteId = noteId.ToLower();
            _createdJobRows.Add((normalizedNoteId, zipFileId));

            // Assert — IMMEDIATELY read the Jobs table for the Queued row
            // The row should exist RIGHT NOW because InsertQueuedJobAsync runs before Enqueue
            TableEntity? jobRow = null;
            try
            {
                var response = await _tableClient.GetEntityAsync<TableEntity>(normalizedNoteId, zipFileId);
                jobRow = response.Value;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                // Row not found — this IS the bug
                jobRow = null;
            }

            jobRow.Should().NotBeNull(
                "InsertQueuedJobAsync should have written a row to the Jobs table " +
                $"with PartitionKey='{normalizedNoteId}' and RowKey='{zipFileId}'. " +
                "If this fails, the Jobs table insert is silently failing.");

            jobRow!["Status"].ToString().Should().Be("Queued",
                "the initial job status should be 'Queued'");
            jobRow["StatusDetails"].ToString().Should().Contain(zipFileId,
                "StatusDetails should reference the zipFileId");
        }

        /// <summary>
        /// POST twice for the same note — both rows should exist in the Jobs table.
        /// </summary>
        [Fact]
        public async Task PostEx1_Twice_CreatesTwoJobRows()
        {
            string noteId = await CreateTestNoteAsync("PostEx1 double-insert test");
            await UploadTextAttachmentAsync(noteId, "file1.txt");

            string normalizedNoteId = noteId.ToLower();

            // First POST
            HttpResponseMessage post1 = await _client.PostAsync(
                $"notes/{noteId}/attachmentzipfilesex1", null);
            post1.StatusCode.Should().Be(HttpStatusCode.Accepted);
            string zip1 = ExtractZipFileIdFromLocation(post1.Headers.Location!);
            _createdJobRows.Add((normalizedNoteId, zip1));

            // Second POST
            HttpResponseMessage post2 = await _client.PostAsync(
                $"notes/{noteId}/attachmentzipfilesex1", null);
            post2.StatusCode.Should().Be(HttpStatusCode.Accepted);
            string zip2 = ExtractZipFileIdFromLocation(post2.Headers.Location!);
            _createdJobRows.Add((normalizedNoteId, zip2));

            // Both rows should exist
            var row1 = await _tableClient.GetEntityAsync<TableEntity>(normalizedNoteId, zip1);
            var row2 = await _tableClient.GetEntityAsync<TableEntity>(normalizedNoteId, zip2);

            row1.Value.Should().NotBeNull("first job row should exist");
            row2.Value.Should().NotBeNull("second job row should exist");
            zip1.Should().NotBe(zip2, "each POST should create a unique zipFileId");
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        //  GET /notes/{noteId}/attachmentzipfiles/jobs/{zipFileId} — job status
        // ═══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// After POST, the GET job status endpoint should return the job row.
        /// </summary>
        [Fact]
        public async Task GetJobStatus_ReturnsQueuedRow_AfterPost()
        {
            string noteId = await CreateTestNoteAsync("GetJobStatus E2E test");
            await UploadTextAttachmentAsync(noteId);

            HttpResponseMessage postResponse = await _client.PostAsync(
                $"notes/{noteId}/attachmentzipfilesex1", null);
            postResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

            string zipFileId = ExtractZipFileIdFromLocation(postResponse.Headers.Location!);
            _createdJobRows.Add((noteId.ToLower(), zipFileId));

            // GET the job status via the API
            HttpResponseMessage getResponse = await _client.GetAsync(
                $"notes/{noteId}/attachmentzipfiles/jobs/{zipFileId}");

            getResponse.StatusCode.Should().Be(HttpStatusCode.OK,
                "GET job status should return 200 for a just-inserted job");

            var jobStatus = await getResponse.Content.ReadFromJsonAsync<JobStatusResponse>(_jsonOptions);
            jobStatus.Should().NotBeNull();
            jobStatus!.ZipFileId.Should().Be(zipFileId);
            // Status may be Queued, InProgress, or Completed depending on timing
            jobStatus.Status.Should().BeOneOf("Queued", "InProgress", "Completed");
        }

        [Fact]
        public async Task GetJobStatus_Returns404_WhenJobDoesNotExist()
        {
            string noteId = await CreateTestNoteAsync("GetJobStatus 404 test");

            HttpResponseMessage response = await _client.GetAsync(
                $"notes/{noteId}/attachmentzipfiles/jobs/nonexistent-{Guid.NewGuid()}.zip");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetJobStatus_Returns404_WhenNoteDoesNotExist()
        {
            HttpResponseMessage response = await _client.GetAsync(
                $"notes/{Guid.NewGuid()}/attachmentzipfiles/jobs/any.zip");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetJobStatus_Returns400_WithInvalidGuid()
        {
            HttpResponseMessage response = await _client.GetAsync(
                "notes/not-a-guid/attachmentzipfiles/jobs/any.zip");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        //  GET /notes/{noteId}/attachmentzipfiles/jobs — all job statuses
        // ═══════════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task GetAllJobStatuses_ReturnsJobList_AfterPost()
        {
            string noteId = await CreateTestNoteAsync("GetAllJobs E2E test");
            await UploadTextAttachmentAsync(noteId);

            HttpResponseMessage postResponse = await _client.PostAsync(
                $"notes/{noteId}/attachmentzipfilesex1", null);
            postResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

            string zipFileId = ExtractZipFileIdFromLocation(postResponse.Headers.Location!);
            _createdJobRows.Add((noteId.ToLower(), zipFileId));

            // GET all jobs for this note
            HttpResponseMessage getResponse = await _client.GetAsync(
                $"notes/{noteId}/attachmentzipfiles/jobs");

            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var jobs = await getResponse.Content.ReadFromJsonAsync<List<JobStatusResponse>>(_jsonOptions);
            jobs.Should().NotBeNull();
            jobs!.Should().ContainSingle(j => j.ZipFileId == zipFileId,
                "the job list should contain the just-created job");
        }

        [Fact]
        public async Task GetAllJobStatuses_Returns404_WhenNoteDoesNotExist()
        {
            HttpResponseMessage response = await _client.GetAsync(
                $"notes/{Guid.NewGuid()}/attachmentzipfiles/jobs");

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task GetAllJobStatuses_Returns400_WithInvalidGuid()
        {
            HttpResponseMessage response = await _client.GetAsync(
                "notes/not-a-guid/attachmentzipfiles/jobs");

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // ═══════════════════════════════════════════════════════════════════════════════
        //  POST basic validation (Ex1 endpoint)
        // ═══════════════════════════════════════════════════════════════════════════════

        [Fact]
        public async Task PostEx1_Returns204_WhenNoteHasNoAttachments()
        {
            string noteId = await CreateTestNoteAsync("No attachments test");

            HttpResponseMessage response = await _client.PostAsync(
                $"notes/{noteId}/attachmentzipfilesex1", null);

            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        [Fact]
        public async Task PostEx1_Returns404_WhenNoteDoesNotExist()
        {
            HttpResponseMessage response = await _client.PostAsync(
                $"notes/{Guid.NewGuid()}/attachmentzipfilesex1", null);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task PostEx1_Returns400_WithInvalidGuid()
        {
            HttpResponseMessage response = await _client.PostAsync(
                "notes/not-a-guid/attachmentzipfilesex1", null);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }
}
