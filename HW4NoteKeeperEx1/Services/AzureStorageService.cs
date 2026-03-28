using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using HW4NoteKeeperEx1.Models;
using HW4NoteKeeperEx1.Settings;
using System.Text.Json;

namespace HW4NoteKeeperEx1.Services
{
    /// <summary>
    /// Result of an attachment deletion operation.
    /// </summary>
    public enum AttachmentDeleteResult
    {
        /// <summary>The blob was found and deleted successfully.</summary>
        Deleted,
        /// <summary>The blob was not present in storage.</summary>
        NotFound,
        /// <summary>The blob was present but could not be deleted due to an error.</summary>
        Error
    }

    /// <summary>
    /// Provides Azure Blob Storage and Queue Storage operations for note attachments and zip archives.
    /// Attachment containers are named with the note's ID; zip containers are named "{noteId}-zip".
    /// </summary>
    public class AzureStorageService
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly QueueServiceClient _queueServiceClient;
        private readonly StorageOperationalSettings _operationalSettings;
        private readonly ILogger<AzureStorageService> _logger;

        /// <summary>
        /// Initializes a new instance of <see cref="AzureStorageService"/>.
        /// </summary>
        public AzureStorageService(
            BlobServiceClient blobServiceClient,
            QueueServiceClient queueServiceClient,
            StorageOperationalSettings operationalSettings,
            ILogger<AzureStorageService> logger)
        {
            _blobServiceClient = blobServiceClient;
            _queueServiceClient = queueServiceClient;
            _operationalSettings = operationalSettings;
            _logger = logger;
        }

        /// <summary>
        /// Uploads a file as a blob to the container associated with the given note.
        /// Creates the container if it does not already exist.
        /// Sets the <c>noteid</c> metadata property on the blob.
        /// </summary>
        /// <param name="noteId">The note's ID (used as the container name).</param>
        /// <param name="attachmentId">The blob name (file name).</param>
        /// <param name="fileData">The file to upload.</param>
        /// <returns><c>true</c> if the blob was newly created; <c>false</c> if it was updated.</returns>
        public async Task<bool> UploadAttachmentAsync(string noteId, string attachmentId, IFormFile fileData)
        {
            BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(noteId);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

            BlobClient blobClient = containerClient.GetBlobClient(attachmentId);
            bool blobAlreadyExists = (await blobClient.ExistsAsync()).Value;

            using Stream stream = fileData.OpenReadStream();
            await blobClient.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = fileData.ContentType },
                Metadata = new Dictionary<string, string> { { "noteid", noteId } }
            });

            _logger.LogInformation(
                "Blob {AttachmentId} {Action} in container {NoteId}",
                attachmentId,
                blobAlreadyExists ? "updated" : "created",
                noteId);

            return !blobAlreadyExists;
        }

        /// <summary>
        /// Deletes the blob with the specified attachment ID from the container associated with the given note.
        /// </summary>
        /// <param name="noteId">The note's ID (container name).</param>
        /// <param name="attachmentId">The blob name to delete.</param>
        /// <returns>
        /// <see cref="AttachmentDeleteResult.Deleted"/> if the blob existed and was deleted;
        /// <see cref="AttachmentDeleteResult.NotFound"/> if the blob did not exist;
        /// <see cref="AttachmentDeleteResult.Error"/> if the blob existed but could not be deleted.
        /// </returns>
        public async Task<AttachmentDeleteResult> DeleteAttachmentAsync(string noteId, string attachmentId)
        {
            BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(noteId);
            BlobClient blobClient = containerClient.GetBlobClient(attachmentId);

            bool exists = (await blobClient.ExistsAsync()).Value;
            if (!exists)
            {
                return AttachmentDeleteResult.NotFound;
            }

            try
            {
                await blobClient.DeleteAsync(DeleteSnapshotsOption.IncludeSnapshots);
                return AttachmentDeleteResult.Deleted;
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Failed to delete blob {AttachmentId} in container {NoteId}", attachmentId, noteId);
                return AttachmentDeleteResult.Error;
            }
        }

        /// <summary>
        /// Returns the number of blobs in the container associated with the given note.
        /// Returns 0 if the container does not exist.
        /// </summary>
        public async Task<int> GetBlobCountAsync(string noteId)
        {
            BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(noteId);

            if (!(await containerClient.ExistsAsync()).Value)
                return 0;

            int count = 0;
            await foreach (BlobItem _ in containerClient.GetBlobsAsync())
                count++;

            return count;
        }

        /// <summary>
        /// Returns whether a blob with the given attachment ID exists in the container for the given note.
        /// </summary>
        public async Task<bool> BlobExistsAsync(string noteId, string attachmentId)
        {
            BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(noteId);
            BlobClient blobClient = containerClient.GetBlobClient(attachmentId);
            return (await blobClient.ExistsAsync()).Value;
        }

        /// <summary>
        /// Uploads a file from a local path as a blob to the container associated with the given note.
        /// Creates the container if it does not already exist.
        /// Sets the <c>noteid</c> metadata property on the blob.
        /// </summary>
        /// <param name="noteId">The note's ID (container name).</param>
        /// <param name="attachmentId">The blob name (file name).</param>
        /// <param name="filePath">Absolute path to the local file to upload.</param>
        /// <param name="contentType">The MIME type of the file.</param>
        public async Task UploadAttachmentFromFileAsync(string noteId, string attachmentId, string filePath, string contentType)
        {
            BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(noteId);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

            BlobClient blobClient = containerClient.GetBlobClient(attachmentId);
            using FileStream stream = File.OpenRead(filePath);
            await blobClient.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Metadata = new Dictionary<string, string> { { "noteid", noteId } }
            });
        }

        /// <summary>
        /// Returns whether a container with the given noteId exists in Azure Blob Storage.
        /// </summary>
        /// <param name="noteId">The note's ID (container name).</param>
        /// <returns><c>true</c> if the container exists; otherwise <c>false</c>.</returns>
        public async Task<bool> ContainerExistsAsync(string noteId)
        {
            BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(noteId);
            return (await containerClient.ExistsAsync()).Value;
        }

        /// <summary>
        /// Downloads an attachment blob from Azure Blob Storage and returns a stream with its content type.
        /// </summary>
        /// <param name="noteId">The note's ID (container name).</param>
        /// <param name="attachmentId">The blob name to download.</param>
        /// <returns>
        /// A tuple containing the blob's content stream and content type if the blob exists;
        /// <c>null</c> if the blob or container does not exist.
        /// </returns>
        public async Task<(Stream stream, string contentType)?> DownloadAttachmentAsync(string noteId, string attachmentId)
        {
            BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(noteId);
            
            // Check if container exists
            if (!(await containerClient.ExistsAsync()).Value)
            {
                return null;
            }

            BlobClient blobClient = containerClient.GetBlobClient(attachmentId);
            
            // Check if blob exists
            if (!(await blobClient.ExistsAsync()).Value)
            {
                return null;
            }

            try
            {
                BlobDownloadResult download = await blobClient.DownloadContentAsync();
                Stream stream = download.Content.ToStream();
                string contentType = download.Details.ContentType ?? "application/octet-stream";
                
                return (stream, contentType);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Failed to download blob {AttachmentId} from container {NoteId}", attachmentId, noteId);
                return null;
            }
        }

        /// <summary>
        /// Lists all attachments in the container associated with the given note, returning summary information for each blob.
        /// </summary>
        /// <param name="noteId">The note's ID (container name).</param>
        /// <returns>
        /// A list of attachment information objects if the container exists;
        /// <c>null</c> if the container does not exist.
        /// </returns>
        public async Task<List<(string attachmentId, string contentType, DateTimeOffset createdDate, DateTimeOffset lastModifiedDate, long length)>?> ListAttachmentsAsync(string noteId)
        {
            BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(noteId);

            // Check if container exists
            if (!(await containerClient.ExistsAsync()).Value)
            {
                return null;
            }

            var attachments = new List<(string, string, DateTimeOffset, DateTimeOffset, long)>();

            await foreach (BlobItem blobItem in containerClient.GetBlobsAsync())
            {
                string attachmentId = blobItem.Name;
                string contentType = blobItem.Properties.ContentType ?? "application/octet-stream";
                DateTimeOffset createdDate = blobItem.Properties.CreatedOn ?? DateTimeOffset.UtcNow;
                DateTimeOffset lastModifiedDate = blobItem.Properties.LastModified ?? DateTimeOffset.UtcNow;
                long length = blobItem.Properties.ContentLength ?? 0;

                attachments.Add((attachmentId, contentType, createdDate, lastModifiedDate, length));
            }

            return attachments;
        }

        // ─── Queue operations ────────────────────────────────────────────────────────

        /// <summary>
        /// Serialises a <see cref="ZipRequest"/> as JSON and enqueues it to the
        /// <c>attachment-zip-requests</c> queue so the Azure Function can create the zip file.
        /// </summary>
        /// <param name="noteId">The note ID whose attachments should be zipped.</param>
        /// <param name="zipFileId">The target blob name for the resulting zip file (e.g. "guid.zip").</param>
        public async Task EnqueueZipRequestAsync(string noteId, string zipFileId)
        {
            QueueClient queueClient = _queueServiceClient.GetQueueClient(_operationalSettings.ZipRequestsQueueName);
            await queueClient.CreateIfNotExistsAsync();

            var message = new ZipRequest { NoteId = noteId, ZipFileId = zipFileId };
            string json = JsonSerializer.Serialize(message);

            await queueClient.SendMessageAsync(json);

            _logger.LogInformation(
                "Enqueued zip request for note {NoteId}, target file {ZipFileId}",
                noteId, zipFileId);
        }

        // ─── Zip container operations ────────────────────────────────────────────────

        /// <summary>
        /// Returns the name of the zip container for the given note ID.
        /// </summary>
        private static string GetZipContainerName(string noteId) => $"{noteId}-zip";

        /// <summary>
        /// Lists all zip blobs in the <c>{noteId}-zip</c> container.
        /// Returns <c>null</c> if the zip container does not exist.
        /// </summary>
        /// <param name="noteId">The note ID (without the "-zip" suffix).</param>
        public async Task<List<(string zipFileId, string contentType, DateTimeOffset createdDate, DateTimeOffset lastModifiedDate, long length)>?> ListZipBlobsAsync(string noteId)
        {
            string containerName = GetZipContainerName(noteId);
            BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            if (!(await containerClient.ExistsAsync()).Value)
                return null;

            var results = new List<(string, string, DateTimeOffset, DateTimeOffset, long)>();
            await foreach (BlobItem blob in containerClient.GetBlobsAsync())
            {
                results.Add((
                    blob.Name,
                    blob.Properties.ContentType ?? "application/zip",
                    blob.Properties.CreatedOn ?? DateTimeOffset.UtcNow,
                    blob.Properties.LastModified ?? DateTimeOffset.UtcNow,
                    blob.Properties.ContentLength ?? 0));
            }

            return results;
        }

        /// <summary>
        /// Downloads a zip blob from the <c>{noteId}-zip</c> container.
        /// Returns <c>null</c> if the container or blob does not exist.
        /// </summary>
        /// <param name="noteId">The note ID.</param>
        /// <param name="zipFileId">The blob name of the zip file (e.g. "guid.zip").</param>
        public async Task<(Stream stream, string contentType)?> DownloadZipBlobAsync(string noteId, string zipFileId)
        {
            string containerName = GetZipContainerName(noteId);
            BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            if (!(await containerClient.ExistsAsync()).Value)
                return null;

            BlobClient blobClient = containerClient.GetBlobClient(zipFileId);
            if (!(await blobClient.ExistsAsync()).Value)
                return null;

            try
            {
                BlobDownloadResult download = await blobClient.DownloadContentAsync();
                return (download.Content.ToStream(), "application/zip");
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Failed to download zip blob {ZipFileId} from container {Container}", zipFileId, containerName);
                return null;
            }
        }

        /// <summary>
        /// Deletes a specific zip blob from the <c>{noteId}-zip</c> container.
        /// Returns <see cref="AttachmentDeleteResult.NotFound"/> if the blob or container does not exist.
        /// </summary>
        /// <param name="noteId">The note ID.</param>
        /// <param name="zipFileId">The blob name to delete.</param>
        public async Task<AttachmentDeleteResult> DeleteZipBlobAsync(string noteId, string zipFileId)
        {
            string containerName = GetZipContainerName(noteId);
            BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            if (!(await containerClient.ExistsAsync()).Value)
                return AttachmentDeleteResult.NotFound;

            BlobClient blobClient = containerClient.GetBlobClient(zipFileId);
            bool exists = (await blobClient.ExistsAsync()).Value;
            if (!exists)
                return AttachmentDeleteResult.NotFound;

            try
            {
                await blobClient.DeleteAsync(DeleteSnapshotsOption.IncludeSnapshots);
                _logger.LogInformation("Deleted zip blob {ZipFileId} from container {Container}", zipFileId, containerName);
                return AttachmentDeleteResult.Deleted;
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Failed to delete zip blob {ZipFileId} from container {Container}", zipFileId, containerName);
                return AttachmentDeleteResult.Error;
            }
        }

        /// <summary>
        /// Deletes a blob container and all its blobs if it exists.
        /// A no-op if the container does not exist.
        /// </summary>
        /// <param name="containerName">The exact container name to delete.</param>
        public async Task DeleteContainerIfExistsAsync(string containerName)
        {
            BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            bool existed = (await containerClient.DeleteIfExistsAsync()).Value;
            if (existed)
                _logger.LogInformation("Deleted blob container {ContainerName}", containerName);
            else
                _logger.LogDebug("Container {ContainerName} did not exist – nothing to delete", containerName);
        }

        /// <summary>
        /// Returns whether the zip container (<c>{noteId}-zip</c>) exists.
        /// </summary>
        /// <param name="noteId">The note ID.</param>
        public async Task<bool> ZipContainerExistsAsync(string noteId)
        {
            string containerName = GetZipContainerName(noteId);
            BlobContainerClient containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            return (await containerClient.ExistsAsync()).Value;
        }
    }
}
