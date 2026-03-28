using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using HW4NoteKeeperEx1.Data;
using HW4NoteKeeperEx1.Settings;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;

namespace HW4NoteKeeperEx1.Data
{
    /// <summary>
    /// Initializes (seeds) Azure Blob Storage with attachment files for individual notes.
    /// Each note is represented by a private blob container named with the note's ID.
    /// </summary>
    /// <remarks>
    /// The attachment mapping mirrors the seed data defined in <see cref="DbInitializer"/>.
    /// </remarks>
    public class AzureStorageInitializer : IAzureStorageInitializer
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly QueueServiceClient _queueServiceClient;
        private readonly StorageOperationalSettings _operationalSettings;
        private readonly ILogger _logger;
        private readonly TelemetryClient _telClient;

        /// <summary>
        /// Maps a note's Summary text to the attachment file names that should be seeded for it.
        /// </summary>
        private static readonly Dictionary<string, string[]> _attachmentMapping = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Running grocery list",       new[] { "MilkAndEggs.png", "Oranges.png" } },
            { "Gift supplies notes",        new[] { "WrappingPaper.png", "Tape.png" } },
            { "Valentine's Day gift ideas", new[] { "Chocolate.png", "Diamonds.png", "NewCar.png" } },
            { "Azure tips",                 new[] { "AzureLogo.png", "AzureTipsAndTricks.pdf" } }
        };

        /// <summary>
        /// Gets the read-only attachment mapping dictionary.
        /// </summary>
        public IReadOnlyDictionary<string, string[]> AttachmentMapping => _attachmentMapping;

        public AzureStorageInitializer(
            BlobServiceClient blobServiceClient,
            QueueServiceClient queueServiceClient,
            StorageOperationalSettings operationalSettings,
            ILogger<AzureStorageInitializer> logger,
            TelemetryClient telClient)
        {
            _blobServiceClient = blobServiceClient;
            _queueServiceClient = queueServiceClient;
            _operationalSettings = operationalSettings;
            _logger = logger;
            _telClient = telClient;
        }

        /// <summary>
        /// Clears all messages from the zip-requests queue and the poison queue.
        /// Called during seeding so stale messages do not trigger the Azure Function after a fresh deploy.
        /// </summary>
        public async Task ClearQueuesAsync()
        {
            string[] queueNames =
            [
                _operationalSettings.ZipRequestsQueueName,
                _operationalSettings.ZipPoisonQueueName
            ];

            foreach (string queueName in queueNames)
            {
                try
                {
                    var queueClient = _queueServiceClient.GetQueueClient(queueName);
                    if ((await queueClient.ExistsAsync()).Value)
                    {
                        await queueClient.ClearMessagesAsync();
                        _logger.LogInformation("Cleared all messages from queue '{QueueName}'.", queueName);
                    }
                    else
                    {
                        _logger.LogInformation("Queue '{QueueName}' does not exist — nothing to clear.", queueName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clear queue '{QueueName}'.", queueName);
                }
            }
        }

        /// <summary>
        /// Deletes all blob containers in the storage account, except those listed in
        /// <see cref="StorageOperationalSettings.ProtectedContainers"/>.
        /// </summary>
        public async Task DeleteAllContainersAsync()
        {
            _logger.LogInformation("Deleting all containers in Azure Blob Storage (protected: [{Protected}])...",
                string.Join(", ", _operationalSettings.ProtectedContainers));
            int deletedCount = 0;
            int skippedCount = 0;

            await foreach (var container in _blobServiceClient.GetBlobContainersAsync())
            {
                if (_operationalSettings.ProtectedContainers.Contains(container.Name, StringComparer.OrdinalIgnoreCase))
                {
                    skippedCount++;
                    _logger.LogInformation("Skipping protected container: {ContainerName}", container.Name);
                    continue;
                }

                try
                {
                    await _blobServiceClient.DeleteBlobContainerAsync(container.Name);
                    deletedCount++;
                    _logger.LogInformation("Deleted container: {ContainerName}", container.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete container: {ContainerName}", container.Name);
                }
            }

            _logger.LogInformation(
                "Deleted {Count} container(s), skipped {Skipped} protected container(s) from Azure Blob Storage.",
                deletedCount, skippedCount);
        }

        /// <summary>
        /// Seeds a single blob container with attachments for a specific note.
        /// Creates a private container named with the note's ID and uploads the associated attachment files.
        /// </summary>
        /// <param name="noteId">The GUID of the note (used as container name).</param>
        /// <param name="summary">The summary text of the note (used to look up attachments in mapping).</param>
        /// <param name="attachmentsDirectory">
        /// Path to the directory containing the attachment files.
        /// Defaults to the <c>AzureStorageAttachments</c> folder under <see cref="AppContext.BaseDirectory"/>.
        /// </param>
        /// <returns>True if container and attachments were seeded successfully; false if any error occurred.</returns>
        public async Task<bool> InitializeAsync(Guid noteId, string summary, string? attachmentsDirectory = null)
        {
            attachmentsDirectory ??= Path.Combine(AppContext.BaseDirectory, "AzureStorageAttachments");

            // Check if this note summary has attachments mapped
            if (!_attachmentMapping.TryGetValue(summary, out string[]? attachmentFiles))
            {
                _logger.LogInformation("Note '{Summary}' has no attachment mapping. Skipping container creation.", summary);
                return true; // Not an error, just no attachments for this note
            }

            string containerName = noteId.ToString().ToLowerInvariant();
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);

            try
            {
                // Create the private container with retry logic for ContainerBeingDeleted errors
                const int maxRetries = 10;
                const int retryDelayMs = 4_000;
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        await containerClient.CreateAsync(Azure.Storage.Blobs.Models.PublicAccessType.None);
                        break;
                    }
                    catch (Azure.RequestFailedException ex) when (ex.ErrorCode == "ContainerBeingDeleted" && attempt < maxRetries)
                    {
                        _logger.LogWarning(
                            "Container '{ContainerName}' is still being deleted (attempt {Attempt}/{Max}). Retrying in {Delay}ms…",
                            containerName, attempt + 1, maxRetries, retryDelayMs);
                        await Task.Delay(retryDelayMs);
                    }
                }

                _logger.LogInformation("Created container '{ContainerName}' for note '{Summary}'.", containerName, summary);

                // Upload each attachment file
                int blobsUploaded = 0;
                foreach (string fileName in attachmentFiles)
                {
                    string filePath = Path.Combine(attachmentsDirectory, fileName);

                    if (!File.Exists(filePath))
                    {
                        _logger.LogWarning("Attachment file '{FilePath}' not found. Skipping blob upload.", filePath);
                        continue;
                    }

                    string contentType = GetContentType(fileName);
                    var blobClient = containerClient.GetBlobClient(fileName);

                    using FileStream stream = File.OpenRead(filePath);
                    await blobClient.UploadAsync(stream, new Azure.Storage.Blobs.Models.BlobUploadOptions
                    {
                        HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = contentType },
                        Metadata = new Dictionary<string, string> { { "noteid", noteId.ToString() } }
                    });

                    blobsUploaded++;
                    _logger.LogInformation("Uploaded blob '{FileName}' to container '{ContainerName}'.", fileName, containerName);
                }

                _telClient.TrackEvent("AzureStorage seeded container",
                    properties: new Dictionary<string, string>
                    {
                        { "ContainerName", containerName },
                        { "NoteSummary", summary },
                        { "BlobsUploaded", blobsUploaded.ToString() }
                    });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to seed container '{ContainerName}' for note '{Summary}'.", containerName, summary);
                _telClient.TrackException(ex, properties: new Dictionary<string, string>
                {
                    { "ContainerName", containerName },
                    { "NoteSummary", summary }
                });
                return false;
            }
        }

        private static string GetContentType(string fileName)
        {
            return Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".png"  => "image/png",
                ".jpg"  => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif"  => "image/gif",
                ".pdf"  => "application/pdf",
                ".txt"  => "text/plain",
                _       => "application/octet-stream"
            };
        }
    }
}
