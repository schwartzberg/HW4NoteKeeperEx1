using Azure;
using Azure.Data.Tables;
using HW4NoteKeeperEx1.Models;
using HW4NoteKeeperEx1.Settings;

namespace HW4NoteKeeperEx1.Services
{
    /// <summary>
    /// Provides CRUD operations on the Azure Storage "Jobs" table for tracking
    /// zip-creation job statuses. Used by the Web API controllers.
    /// </summary>
    public class JobsTableService
    {
        private readonly TableClient _tableClient;
        private readonly ILogger<JobsTableService> _logger;

        /// <summary>
        /// Initializes a new instance of <see cref="JobsTableService"/>.
        /// </summary>
        public JobsTableService(TableClient tableClient, ILogger<JobsTableService> logger)
        {
            _tableClient = tableClient;
            _logger = logger;
        }

        /// <summary>
        /// Normalizes noteId to lowercase to ensure consistent PartitionKey casing
        /// across Web API and Azure Functions (which lowercases for container names).
        /// </summary>
        private static string NormalizeNoteId(string noteId) => noteId.ToLowerInvariant();

        /// <summary>
        /// Inserts a new job row with Status=<c>Queued</c> and the appropriate StatusDetails (§2.3.1, §2.4.1).
        /// </summary>
        /// <param name="noteId">The note ID (partition key).</param>
        /// <param name="zipFileId">The zip file ID (row key).</param>
        public virtual async Task InsertQueuedJobAsync(string noteId, string zipFileId)
        {
            string normalizedNoteId = NormalizeNoteId(noteId);
            var entity = new JobEntity
            {
                PartitionKey = normalizedNoteId,
                RowKey = zipFileId,
                Status = "Queued",
                StatusDetails = $"Queued: Zip File Id: {zipFileId} NoteId: {normalizedNoteId}"
            };

            await _tableClient.AddEntityAsync(entity);
            _logger.LogInformation(
                "Inserted Queued job row – NoteId={NoteId}, ZipFileId={ZipFileId}",
                normalizedNoteId, zipFileId);
        }

        /// <summary>
        /// Retrieves a single job entity by noteId and zipFileId.
        /// Returns <c>null</c> if the row does not exist.
        /// </summary>
        public virtual async Task<JobEntity?> GetJobAsync(string noteId, string zipFileId)
        {
            try
            {
                string normalizedNoteId = NormalizeNoteId(noteId);
                var response = await _tableClient.GetEntityAsync<JobEntity>(normalizedNoteId, zipFileId);
                return response.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        /// <summary>
        /// Retrieves all job entities for the given noteId (partition key).
        /// Returns an empty list if no rows exist.
        /// </summary>
        public virtual async Task<List<JobEntity>> GetJobsByNoteIdAsync(string noteId)
        {
            string normalizedNoteId = NormalizeNoteId(noteId);
            var results = new List<JobEntity>();
            var queryResults = _tableClient.QueryAsync<JobEntity>(
                filter: $"PartitionKey eq '{normalizedNoteId}'");

            await foreach (var entity in queryResults)
            {
                results.Add(entity);
            }

            return results;
        }

        /// <summary>
        /// Returns <c>true</c> if any job for the given noteId has Status=<c>InProgress</c>.
        /// Used by the enhanced DELETE (§4.1.4) to determine if a 409 Conflict should be returned.
        /// </summary>
        public async Task<bool> HasInProgressJobsAsync(string noteId)
        {
            string normalizedNoteId = NormalizeNoteId(noteId);
            var queryResults = _tableClient.QueryAsync<JobEntity>(
                filter: $"PartitionKey eq '{normalizedNoteId}' and Status eq 'InProgress'",
                maxPerPage: 1);

            await foreach (var _ in queryResults)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Deletes all job rows for the given noteId from the Jobs table.
        /// Returns the number of rows deleted.
        /// Per §4.1.2, callers should log info if count is 0.
        /// Per §4.1.3, callers should catch exceptions and log errors without failing the request.
        /// </summary>
        public async Task<int> DeleteJobsByNoteIdAsync(string noteId)
        {
            string normalizedNoteId = NormalizeNoteId(noteId);
            var jobs = await GetJobsByNoteIdAsync(normalizedNoteId);
            int deletedCount = 0;

            foreach (var job in jobs)
            {
                await _tableClient.DeleteEntityAsync(job.PartitionKey, job.RowKey);
                deletedCount++;
            }

            _logger.LogInformation(
                "Deleted {Count} job row(s) for NoteId={NoteId}", deletedCount, normalizedNoteId);
            return deletedCount;
        }

        /// <summary>
        /// Deletes all rows from the Jobs table. Used during seeding to ensure clean state.
        /// </summary>
        public async Task ClearAllJobsAsync()
        {
            int deletedCount = 0;
            var queryResults = _tableClient.QueryAsync<JobEntity>(select: new[] { "PartitionKey", "RowKey" });

            await foreach (var entity in queryResults)
            {
                await _tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
                deletedCount++;
            }

            _logger.LogInformation("Cleared {Count} row(s) from Jobs table during seeding", deletedCount);
        }
    }
}
