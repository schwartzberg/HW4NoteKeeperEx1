using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HW4AzureFunctionsEx1.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO.Compression;

namespace HW4AzureFunctionsEx1
{
    /// <summary>
    /// Contains the business logic for creating zip archives from note attachments.
    /// Called by both the queue-triggered function and the HTTP test function.
    /// Enhanced for Extra Credit 1 to update the Azure Storage Jobs table
    /// with InProgress, Completed, and Failed status rows.
    /// </summary>
    public class AttachmentZipProcessor
    {
        private readonly BlobStorageHelper _blobStorageHelper;
        private readonly TableStorageHelper _tableStorageHelper;
        private readonly ILogger<AttachmentZipProcessor> _logger;
        private readonly string? _sqlConnectionString;

        public AttachmentZipProcessor(
            BlobStorageHelper blobStorageHelper,
            TableStorageHelper tableStorageHelper,
            ILogger<AttachmentZipProcessor> logger,
            IConfiguration configuration)
        {
            _blobStorageHelper = blobStorageHelper;
            _tableStorageHelper = tableStorageHelper;
            _logger = logger;
            _sqlConnectionString = configuration.GetConnectionString("DefaultConnection");
        }

        /// <summary>
        /// Processes a zip request: downloads all blobs from the note's attachment container,
        /// creates a zip archive, and uploads it to the {noteId}-zip container.
        /// Updates the Jobs table at each stage (InProgress → Completed/Failed).
        /// </summary>
        public async Task ProcessAsync(ZipRequest request)
        {
            if (request is null
                || !Guid.TryParse(request.NoteId, out _)
                || string.IsNullOrWhiteSpace(request.ZipFileId))
            {
                _logger.LogError(
                    "Invalid ZipRequest payload – NoteId={NoteId}, ZipFileId={ZipFileId}.",
                    request?.NoteId, request?.ZipFileId);
                throw new InvalidOperationException($"Invalid ZipRequest: NoteId={request?.NoteId}, ZipFileId={request?.ZipFileId}");
            }

            string noteId = request.NoteId.ToLower();
            string zipFileId = request.ZipFileId;
            string zipContainerName = $"{noteId}-zip";

            _logger.LogInformation(
                "Processing zip request – NoteId={NoteId}, ZipFileId={ZipFileId}, ZipContainer={ZipContainer}",
                noteId, zipFileId, zipContainerName);

            // §4.1.5 – Check 1: Verify the Queued row still exists before starting
            if (!await QueuedJobExistsAsync(noteId, zipFileId))
            {
                _logger.LogError(
                    "Aborting zip processing: Queued job row not found for NoteId={NoteId}, ZipFileId={ZipFileId}. The job may have been cancelled by a DELETE.",
                    noteId, zipFileId);
                return;
            }

            // §2.3.2 – Update status to InProgress
            await UpdateJobStatusAsync(noteId, zipFileId, "InProgress",
                $"In Progress: Zip File Id: {zipFileId} NoteId: {noteId}");

            try
            {
                // Verify the note still exists in the database before doing any storage work
                if (!await NoteExistsInDatabaseAsync(request.NoteId))
                {
                    _logger.LogError(
                        "The note {NoteId} can't be found for the requested compression operation.",
                        noteId);
                    await UpdateJobStatusAsync(noteId, zipFileId, "Failed",
                        $"Failed: Zip File Id: {zipFileId} NoteId: {noteId}");
                    return;
                }

                BlobServiceClient blobServiceClient = _blobStorageHelper.Client;

                // Get the attachment container
                BlobContainerClient attachmentContainer = blobServiceClient.GetBlobContainerClient(noteId);

                if (!(await attachmentContainer.ExistsAsync()).Value)
                {
                    _logger.LogWarning(
                        "Attachment container '{Container}' does not exist for note {NoteId}. Nothing to zip.",
                        noteId, noteId);
                    await UpdateJobStatusAsync(noteId, zipFileId, "Failed",
                        $"Failed: Zip File Id: {zipFileId} NoteId: {noteId}");
                    return;
                }

                // Collect all blobs in the attachment container
                var blobNames = new List<string>();
                await foreach (BlobItem blobItem in attachmentContainer.GetBlobsAsync())
                    blobNames.Add(blobItem.Name);

                if (blobNames.Count == 0)
                {
                    _logger.LogWarning("No blobs found in container '{Container}' – zip will not be created.", noteId);
                    await UpdateJobStatusAsync(noteId, zipFileId, "Failed",
                        $"Failed: Zip File Id: {zipFileId} NoteId: {noteId}");
                    return;
                }

                _logger.LogInformation("Found {Count} blob(s) in container '{Container}'", blobNames.Count, noteId);

                // Build zip archive in memory
                using var zipStream = new MemoryStream();
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (string blobName in blobNames)
                    {
                        BlobClient blobClient = attachmentContainer.GetBlobClient(blobName);
                        try
                        {
                            var downloadResult = await blobClient.DownloadContentAsync();
                            ZipArchiveEntry entry = archive.CreateEntry(blobName, CompressionLevel.Optimal);
                            using Stream entryStream = entry.Open();
                            await downloadResult.Value.Content.ToStream().CopyToAsync(entryStream);
                            _logger.LogDebug("Added '{BlobName}' to zip archive", blobName);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to add blob '{BlobName}' to zip archive", blobName);
                            throw;
                        }
                    }
                }

                // §4.1.5 – Check 2: Re-verify Queued/InProgress row before creating zip container
                if (!await QueuedJobExistsAsync(noteId, zipFileId))
                {
                    _logger.LogError(
                        "Aborting zip processing before container creation: job row not found for NoteId={NoteId}, ZipFileId={ZipFileId}. The job may have been cancelled by a DELETE.",
                        noteId, zipFileId);
                    return;
                }

                // Upload the zip archive to the {noteId}-zip container
                zipStream.Seek(0, SeekOrigin.Begin);

                BlobContainerClient zipContainer = blobServiceClient.GetBlobContainerClient(zipContainerName);
                await zipContainer.CreateIfNotExistsAsync();

                BlobClient zipBlobClient = zipContainer.GetBlobClient(zipFileId);
                await zipBlobClient.UploadAsync(zipStream, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/zip" }
                });

                // §2.3.3 – Update status to Completed
                await UpdateJobStatusAsync(noteId, zipFileId, "Completed",
                    $"Completed: zipFileId: {zipFileId} containerId: {zipContainerName}");

                _logger.LogInformation(
                    "Successfully uploaded zip '{ZipFileId}' to container '{ZipContainer}' for note {NoteId}",
                    zipFileId, zipContainerName, noteId);
            }
            catch (Exception ex)
            {
                // §2.3.4 – Update status to Failed
                _logger.LogError(ex, "Error processing zip request for NoteId={NoteId}, ZipFileId={ZipFileId}", noteId, zipFileId);
                await UpdateJobStatusSafeAsync(noteId, zipFileId, "Failed",
                    $"Failed: Zip File Id: {zipFileId} NoteId: {noteId}");
                throw;
            }
        }

        /// <summary>
        /// Checks whether the job row still exists in the Jobs table.
        /// Used at two checkpoints per §4.1.5 to detect if a DELETE cancelled this job.
        /// </summary>
        private async Task<bool> QueuedJobExistsAsync(string noteId, string zipFileId)
        {
            try
            {
                var tableClient = _tableStorageHelper.Client;
                var response = await tableClient.GetEntityAsync<JobEntity>(noteId, zipFileId);
                return response.Value != null;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking Jobs table for NoteId={NoteId}, ZipFileId={ZipFileId}", noteId, zipFileId);
                return false;
            }
        }

        /// <summary>
        /// Updates the job entity status using upsert (merge) so partial updates don't fail.
        /// </summary>
        private async Task UpdateJobStatusAsync(string noteId, string zipFileId, string status, string statusDetails)
        {
            try
            {
                var tableClient = _tableStorageHelper.Client;
                var entity = new JobEntity
                {
                    PartitionKey = noteId,
                    RowKey = zipFileId,
                    Status = status,
                    StatusDetails = statusDetails
                };
                await tableClient.UpsertEntityAsync(entity, Azure.Data.Tables.TableUpdateMode.Merge);

                _logger.LogInformation(
                    "Updated job status to {Status} – NoteId={NoteId}, ZipFileId={ZipFileId}",
                    status, noteId, zipFileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to update Jobs table to {Status} for NoteId={NoteId}, ZipFileId={ZipFileId}",
                    status, noteId, zipFileId);
                throw;
            }
        }

        /// <summary>
        /// Updates job status without throwing – used in catch blocks to avoid masking the original exception.
        /// </summary>
        private async Task UpdateJobStatusSafeAsync(string noteId, string zipFileId, string status, string statusDetails)
        {
            try
            {
                await UpdateJobStatusAsync(noteId, zipFileId, status, statusDetails);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to update Jobs table to {Status} (safe) for NoteId={NoteId}, ZipFileId={ZipFileId}",
                    status, noteId, zipFileId);
            }
        }

        private async Task<bool> NoteExistsInDatabaseAsync(string noteId)
        {
            if (string.IsNullOrWhiteSpace(_sqlConnectionString))
            {
                _logger.LogWarning("No SQL connection string configured – skipping DB existence check for note {NoteId}.", noteId);
                return true;
            }

            try
            {
                await using var connection = new SqlConnection(_sqlConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(1) FROM Note WHERE Id = @NoteId";
                command.Parameters.AddWithValue("@NoteId", Guid.Parse(noteId));
                int count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking database for NoteId {NoteId} – retrying message.", noteId);
                throw;
            }
        }
    }
}
