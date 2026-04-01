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
    /// Original (legacy) processor for the non-Ex1 queue-triggered function.
    /// Does NOT use the Jobs table — preserves the original behavior exactly.
    /// </summary>
    public class AttachmentZipProcessorLegacy
    {
        private readonly BlobStorageHelper _blobStorageHelper;
        private readonly ILogger<AttachmentZipProcessorLegacy> _logger;
        private readonly string? _sqlConnectionString;

        public AttachmentZipProcessorLegacy(BlobStorageHelper blobStorageHelper, ILogger<AttachmentZipProcessorLegacy> logger, IConfiguration configuration)
        {
            _blobStorageHelper = blobStorageHelper;
            _logger = logger;
            _sqlConnectionString = configuration.GetConnectionString("DefaultConnection");
        }

        /// <summary>
        /// Processes a zip request: downloads all blobs from the note's attachment container,
        /// creates a zip archive, and uploads it to the {noteId}-zip container.
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

            // Verify the note still exists in the database before doing any storage work
            if (!await NoteExistsInDatabaseAsync(request.NoteId))
            {
                _logger.LogError(
                    "The note {NoteId} can't be found for the requested compression operation.",
                    noteId);
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
                return;
            }

            // Collect all blobs in the attachment container
            var blobNames = new List<string>();
            await foreach (BlobItem blobItem in attachmentContainer.GetBlobsAsync())
                blobNames.Add(blobItem.Name);

            if (blobNames.Count == 0)
            {
                _logger.LogWarning("No blobs found in container '{Container}' – zip will not be created.", noteId);
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

            // Upload the zip archive to the {noteId}-zip container
            zipStream.Seek(0, SeekOrigin.Begin);

            BlobContainerClient zipContainer = blobServiceClient.GetBlobContainerClient(zipContainerName);
            await zipContainer.CreateIfNotExistsAsync();

            BlobClient zipBlobClient = zipContainer.GetBlobClient(zipFileId);
            await zipBlobClient.UploadAsync(zipStream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/zip" }
            });

            _logger.LogInformation(
                "Successfully uploaded zip '{ZipFileId}' to container '{ZipContainer}' for note {NoteId}",
                zipFileId, zipContainerName, noteId);
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
