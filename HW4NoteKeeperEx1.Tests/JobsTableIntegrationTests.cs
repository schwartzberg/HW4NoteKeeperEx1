using Azure.Data.Tables;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HW4NoteKeeperEx1.Tests
{
    /// <summary>
    /// Integration tests for the Jobs table in Azure Table Storage.
    /// Each test inserts a row, verifies it exists, then deletes it — leaving no residue.
    /// Runs against live Azure storage (same account used by the Function App).
    /// </summary>
    [Collection("Sequential")]
    [Trait("Category", "E2E")]
    public class JobsTableIntegrationTests
    {
        private const string TableUri = "https://st4hw3.table.core.windows.net";
        private const string TableName = "Jobs";

        private readonly TableClient _tableClient;

        public JobsTableIntegrationTests()
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddUserSecrets<JobsTableIntegrationTests>()
                .Build();

            // Reuse the tenant ID already present in other test user secrets
            string tenantId = config["StorageAccountSettings:TenantId"] ?? "d607a394-7dc0-4c7d-8b9e-a9ed69c728b9";

            // Local dev: use cached Visual Studio / VS Code credentials (same as other E2E tests).
            // In CI/Azure: managed identity takes over.
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

            var serviceClient = new TableServiceClient(new Uri(TableUri), new DefaultAzureCredential(credentialOptions));
            _tableClient = serviceClient.GetTableClient(TableName);
        }

        /// <summary>
        /// Verifies a JobEntity can be inserted, read back with correct field values, then deleted.
        /// This proves the table is reachable with the configured credentials and the row-key schema works.
        /// </summary>
        [Fact]
        public async Task Jobs_Insert_CanReadBack_ThenDelete()
        {
            // Arrange – use GUIDs so this row never collides with real data
            string partitionKey = $"test-note-{Guid.NewGuid()}";
            string rowKey = $"test-zip-{Guid.NewGuid()}";

            var entity = new TableEntity(partitionKey, rowKey)
            {
                ["Status"] = "Queued",
                ["StatusDetails"] = "Integration test – insert/read/delete"
            };

            try
            {
                // Act – Insert
                var insertResponse = await _tableClient.AddEntityAsync(entity);
                insertResponse.Status.Should().BeLessThan(300, "AddEntity should return 2xx");

                // Act – Read back
                var read = await _tableClient.GetEntityAsync<TableEntity>(partitionKey, rowKey);

                // Assert
                read.Value.Should().NotBeNull();
                read.Value.PartitionKey.Should().Be(partitionKey);
                read.Value.RowKey.Should().Be(rowKey);
                read.Value["Status"].ToString().Should().Be("Queued");
                read.Value["StatusDetails"].ToString().Should().Be("Integration test – insert/read/delete");
            }
            finally
            {
                // Cleanup – always remove the test row, even if assertions failed
                try { await _tableClient.DeleteEntityAsync(partitionKey, rowKey); } catch { /* best effort */ }
            }
        }

        /// <summary>
        /// Verifies a job entity can progress through status updates: Queued → InProgress → Completed.
        /// Mirrors the lifecycle the Azure Function uses when processing a zip request.
        /// </summary>
        [Fact]
        public async Task Jobs_StatusLifecycle_QueuedToInProgressToCompleted()
        {
            string partitionKey = $"test-note-{Guid.NewGuid()}";
            string rowKey = $"test-zip-{Guid.NewGuid()}";

            var entity = new TableEntity(partitionKey, rowKey)
            {
                ["Status"] = "Queued",
                ["StatusDetails"] = "Job queued"
            };

            try
            {
                await _tableClient.AddEntityAsync(entity);

                // Transition: Queued → InProgress
                entity["Status"] = "InProgress";
                entity["StatusDetails"] = "Creating zip archive";
                await _tableClient.UpdateEntityAsync(entity, Azure.ETag.All, TableUpdateMode.Replace);

                var midRead = await _tableClient.GetEntityAsync<TableEntity>(partitionKey, rowKey);
                midRead.Value["Status"].ToString().Should().Be("InProgress");

                // Transition: InProgress → Completed
                entity["Status"] = "Completed";
                entity["StatusDetails"] = "Zip archive ready";
                await _tableClient.UpdateEntityAsync(entity, Azure.ETag.All, TableUpdateMode.Replace);

                var finalRead = await _tableClient.GetEntityAsync<TableEntity>(partitionKey, rowKey);
                finalRead.Value["Status"].ToString().Should().Be("Completed");
                finalRead.Value["StatusDetails"].ToString().Should().Be("Zip archive ready");
            }
            finally
            {
                try { await _tableClient.DeleteEntityAsync(partitionKey, rowKey); } catch { /* best effort */ }
            }
        }

        /// <summary>
        /// Verifies that attempting to read a non-existent row throws a RequestFailedException with 404.
        /// </summary>
        [Fact]
        public async Task Jobs_GetNonExistentRow_Throws404()
        {
            string partitionKey = $"ghost-note-{Guid.NewGuid()}";
            string rowKey = $"ghost-zip-{Guid.NewGuid()}";

            Func<Task> act = async () =>
                await _tableClient.GetEntityAsync<TableEntity>(partitionKey, rowKey);

            await act.Should().ThrowAsync<Azure.RequestFailedException>()
                .Where(ex => ex.Status == 404);
        }
    }
}
