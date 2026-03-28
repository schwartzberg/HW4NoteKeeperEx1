using HW4NoteKeeperEx1.CustomSettings;
using HW4NoteKeeperEx1.Data;
using HW4NoteKeeperEx1.RequestAndResultObjects;
using HW4NoteKeeperEx1.Services;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HW4NoteKeeperEx1.Controllers
{
    /// <summary>
    /// Manages attachment blobs stored in Azure Blob Storage for a given note.
    /// Each note's attachments reside in a private container named with the note's ID.
    /// </summary>
    [ApiController]
    [Route("notes/{noteId}/attachments")]
    public class NoteKeeperAttachmentController : ControllerBase
    {
        private readonly MyDatabaseContext _context;
        private readonly AzureStorageService _storageService;
        private readonly NoteLimits _noteLimits;
        private readonly ILogger<NoteKeeperAttachmentController> _logger;
        private readonly TelemetryClient _telemetryClient;

        public NoteKeeperAttachmentController(
            MyDatabaseContext context,
            AzureStorageService storageService,
            NoteLimits noteLimits,
            ILogger<NoteKeeperAttachmentController> logger,
            TelemetryClient telemetryClient)
        {
            _context = context;
            _storageService = storageService;
            _noteLimits = noteLimits;
            _logger = logger;
            _telemetryClient = telemetryClient;
        }

        /// <summary>
        /// Creates or updates an attachment blob in Azure Blob Storage for the specified note.
        /// </summary>
        /// <param name="noteId">The ID of the note (container name). Must be a valid GUID.</param>
        /// <param name="attachmentId">
        /// The name of the attachment (blob ID). Must not be null, empty, or whitespace.
        /// </param>
        /// <param name="fileData">The file to upload.</param>
        /// <returns>
        /// 201 Created with a Location header if the attachment was newly created;
        /// 204 No Content if an existing attachment was updated;
        /// 400 Bad Request if any parameter is invalid;
        /// 403 Forbidden if adding this attachment would exceed the MaxAttachments limit.
        /// </returns>
        [HttpPut("{attachmentId}")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> PutAttachment(string noteId, string attachmentId, IFormFile fileData)
        {
            // Validate noteId
            if (string.IsNullOrWhiteSpace(noteId) || !Guid.TryParse(noteId, out Guid noteGuid))
            {
                _logger.LogWarning("PutAttachment called with invalid noteId: '{NoteId}'", noteId);
                
                // Track validation error
                var properties = new Dictionary<string, string>
                {
                    { "ValidationError", "Invalid noteId format" },
                    { "InputPayload", JsonSerializer.Serialize(new { noteId, attachmentId, fileSize = fileData?.Length ?? 0 }) }
                };
                _telemetryClient.TrackTrace("PutAttachment - Validation Error", SeverityLevel.Warning, properties);
                
                return BadRequest();
            }

            // Validate attachmentId
            if (string.IsNullOrWhiteSpace(attachmentId))
            {
                _logger.LogWarning("PutAttachment called with null or empty attachmentId for note {NoteId}", noteId);
                
                // Track validation error
                var properties = new Dictionary<string, string>
                {
                    { "ValidationError", "AttachmentId is null or empty" },
                    { "InputPayload", JsonSerializer.Serialize(new { noteId, attachmentId, fileSize = fileData?.Length ?? 0 }) }
                };
                _telemetryClient.TrackTrace("PutAttachment - Validation Error", SeverityLevel.Warning, properties);
                
                return BadRequest();
            }

            // Validate fileData
            if (fileData == null || fileData.Length == 0)
            {
                _logger.LogWarning("PutAttachment called with null or empty fileData for note {NoteId}", noteId);
                
                // Track validation error
                var properties = new Dictionary<string, string>
                {
                    { "ValidationError", "FileData is null or empty" },
                    { "InputPayload", JsonSerializer.Serialize(new { noteId, attachmentId }) }
                };
                _telemetryClient.TrackTrace("PutAttachment - Validation Error", SeverityLevel.Warning, properties);
                
                return BadRequest();
            }

            try
            {
                // Check attachment limit — only enforce when the blob does not already exist
                bool blobAlreadyExists = await _storageService.BlobExistsAsync(noteId, attachmentId);
                if (!blobAlreadyExists)
                {
                    int currentCount = await _storageService.GetBlobCountAsync(noteId);
                    if (currentCount >= _noteLimits.MaxAttachments)
                    {
                        _logger.LogWarning(
                            "PutAttachment: attachment limit of {Limit} reached for note {NoteId}",
                            _noteLimits.MaxAttachments, noteId);

                        return Problem(
                            title: "Attachment limit reached",
                            statusCode: StatusCodes.Status403Forbidden,
                            detail: $"Attachment limit reached MaxAttachments: [{_noteLimits.MaxAttachments}]");
                    }
                }

                // Upload (create or update)
                bool wasCreated = await _storageService.UploadAttachmentAsync(noteId, attachmentId, fileData);

                if (wasCreated)
                {
                    // Track AttachmentCreated event
                    var properties = new Dictionary<string, string>
                    {
                        { "attachmentid", attachmentId }
                    };
                    var metrics = new Dictionary<string, double>
                    {
                        { "AttachmentSize", fileData.Length }
                    };
                    _telemetryClient.TrackEvent("AttachmentCreated", properties, metrics);

                    string locationUrl = $"{Request.Scheme}://{Request.Host}/notes/{noteId}/attachments/{attachmentId}";
                    return Created(locationUrl, null);
                }
                else
                {
                    // Track AttachmentUpdated event
                    var properties = new Dictionary<string, string>
                    {
                        { "attachmentid", attachmentId }
                    };
                    var metrics = new Dictionary<string, double>
                    {
                        { "AttachmentSize", fileData.Length }
                    };
                    _telemetryClient.TrackEvent("AttachmentUpdated", properties, metrics);

                    return NoContent();
                }
            }
            catch (Exception ex)
            {
                // Track exception
                var properties = new Dictionary<string, string>
                {
                    { "ExceptionMessage", ex.Message },
                    { "InputPayload", JsonSerializer.Serialize(new { noteId, attachmentId, fileSize = fileData.Length }) }
                };
                _telemetryClient.TrackException(ex, properties);

                _logger.LogError(ex, "Error uploading attachment {AttachmentId} for note {NoteId}", attachmentId, noteId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Deletes the specified attachment blob from Azure Blob Storage.
        /// </summary>
        /// <param name="noteId">The ID of the note (container name). Must be a valid GUID.</param>
        /// <param name="attachmentId">The name of the attachment to delete.</param>
        /// <returns>
        /// 204 No Content if the attachment was deleted or did not exist;
        /// 400 Bad Request if any parameter is invalid;
        /// 404 Not Found if the container (note) does not exist;
        /// 500 Internal Server Error if the attachment was present but could not be deleted.
        /// </returns>
        [HttpDelete("{attachmentId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteAttachment(string noteId, string attachmentId)
        {
            // Validate noteId
            if (string.IsNullOrWhiteSpace(noteId) || !Guid.TryParse(noteId, out Guid noteGuid))
            {
                _logger.LogWarning("DeleteAttachment called with invalid noteId: '{NoteId}'", noteId);
                
                // Track validation error
                var properties = new Dictionary<string, string>
                {
                    { "ValidationError", "Invalid noteId format" },
                    { "InputPayload", JsonSerializer.Serialize(new { noteId, attachmentId }) }
                };
                _telemetryClient.TrackTrace("DeleteAttachment - Validation Error", SeverityLevel.Warning, properties);
                
                return BadRequest();
            }

            // Validate attachmentId
            if (string.IsNullOrWhiteSpace(attachmentId))
            {
                _logger.LogWarning("DeleteAttachment called with null or empty attachmentId for note {NoteId}", noteId);
                
                // Track validation error
                var properties = new Dictionary<string, string>
                {
                    { "ValidationError", "AttachmentId is null or empty" },
                    { "InputPayload", JsonSerializer.Serialize(new { noteId, attachmentId }) }
                };
                _telemetryClient.TrackTrace("DeleteAttachment - Validation Error", SeverityLevel.Warning, properties);
                
                return BadRequest();
            }

            try
            {
                // Check if container (note) exists
                bool containerExists = await _storageService.ContainerExistsAsync(noteId);
                if (!containerExists)
                {
                    _logger.LogWarning("DeleteAttachment: container for note {NoteId} not found", noteId);
                    return NotFound(new { message = "Note not found" });
                }

                AttachmentDeleteResult result = await _storageService.DeleteAttachmentAsync(noteId, attachmentId);

                switch (result)
                {
                    case AttachmentDeleteResult.Deleted:
                        _logger.LogInformation(
                            "Attachment '{AttachmentId}' deleted from note {NoteId}",
                            attachmentId, noteId);
                        return NoContent();

                    case AttachmentDeleteResult.NotFound:
                        _logger.LogWarning(
                            "Attachment '{AttachmentId}' not found in note {NoteId}; returning 204",
                            attachmentId, noteId);
                        return NoContent();

                    case AttachmentDeleteResult.Error:
                        _logger.LogError(
                            "Failed to delete attachment '{AttachmentId}' from note {NoteId}",
                            attachmentId, noteId);
                        return StatusCode(StatusCodes.Status500InternalServerError);

                    default:
                        return StatusCode(StatusCodes.Status500InternalServerError);
                }
            }
            catch (Exception ex)
            {
                // Track exception
                var properties = new Dictionary<string, string>
                {
                    { "ExceptionMessage", ex.Message },
                    { "InputPayload", JsonSerializer.Serialize(new { noteId, attachmentId }) }
                };
                _telemetryClient.TrackException(ex, properties);

                _logger.LogError(ex, "Error deleting attachment {AttachmentId} for note {NoteId}", attachmentId, noteId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Retrieves an attachment from Azure Blob Storage.
        /// </summary>
        /// <param name="noteId">The ID of the note (container name). Must be a valid GUID.</param>
        /// <param name="attachmentId">The name of the attachment to retrieve.</param>
        /// <returns>
        /// 200 OK with the attachment file stream if successful;
        /// 400 Bad Request if any parameter is invalid;
        /// 404 Not Found if the note or attachment does not exist.
        /// </returns>
        [HttpGet("{attachmentId}")]
        [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetAttachment(string noteId, string attachmentId)
        {
            // Validate noteId
            if (string.IsNullOrWhiteSpace(noteId) || !Guid.TryParse(noteId, out Guid noteGuid))
            {
                _logger.LogWarning("GetAttachment called with invalid noteId: '{NoteId}'", noteId);
                
                // Track validation error
                var properties = new Dictionary<string, string>
                {
                    { "ValidationError", "Invalid noteId format" },
                    { "InputPayload", JsonSerializer.Serialize(new { noteId, attachmentId }) }
                };
                _telemetryClient.TrackTrace("GetAttachment - Validation Error", SeverityLevel.Warning, properties);
                
                return BadRequest();
            }

            // Validate attachmentId
            if (string.IsNullOrWhiteSpace(attachmentId))
            {
                _logger.LogWarning("GetAttachment called with null or empty attachmentId for note {NoteId}", noteId);
                
                // Track validation error
                var properties = new Dictionary<string, string>
                {
                    { "ValidationError", "AttachmentId is null or empty" },
                    { "InputPayload", JsonSerializer.Serialize(new { noteId, attachmentId }) }
                };
                _telemetryClient.TrackTrace("GetAttachment - Validation Error", SeverityLevel.Warning, properties);
                
                return BadRequest();
            }

            try
            {
                // Try to download the attachment
                var downloadResult = await _storageService.DownloadAttachmentAsync(noteId, attachmentId);

                if (downloadResult == null)
                {
                    _logger.LogWarning(
                        "GetAttachment: attachment '{AttachmentId}' not found in note {NoteId}",
                        attachmentId, noteId);
                    return NotFound(new { message = "Attachment not found" });
                }

                var (stream, contentType) = downloadResult.Value;

                _logger.LogInformation(
                    "Attachment '{AttachmentId}' retrieved from note {NoteId}",
                    attachmentId, noteId);

                // Return the file with Content-Disposition header set to attachment with filename
                return File(stream, contentType, attachmentId, enableRangeProcessing: false);
            }
            catch (Exception ex)
            {
                // Track exception
                var properties = new Dictionary<string, string>
                {
                    { "ExceptionMessage", ex.Message },
                    { "InputPayload", JsonSerializer.Serialize(new { noteId, attachmentId }) }
                };
                _telemetryClient.TrackException(ex, properties);

                _logger.LogError(ex, "Error retrieving attachment {AttachmentId} for note {NoteId}", attachmentId, noteId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Retrieves all attachment blob IDs with summary information for the associated note.
        /// </summary>
        /// <param name="noteId">The ID of the note (container name). Must be a valid GUID.</param>
        /// <returns>
        /// 200 OK with a list of attachment information (empty list if no attachments exist);
        /// 400 Bad Request if noteId is invalid;
        /// 404 Not Found if the container does not exist.
        /// </returns>
        /// <remarks>
        /// Note: This endpoint returns 404 if the container doesn't exist, going beyond the specification requirement 
        /// for consistency with GetAttachment behavior. The spec only requires 200 OK regardless of whether attachments exist.
        /// </remarks>
        [HttpGet]
        [ProducesResponseType(typeof(List<AttachmentInfoResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetAllAttachments(string noteId)
        {
            // Validate noteId
            if (string.IsNullOrWhiteSpace(noteId) || !Guid.TryParse(noteId, out Guid noteGuid))
            {
                _logger.LogWarning("GetAllAttachments called with invalid noteId: '{NoteId}'", noteId);
                
                // Track validation error
                var properties = new Dictionary<string, string>
                {
                    { "ValidationError", "Invalid noteId format" },
                    { "InputPayload", JsonSerializer.Serialize(new { noteId }) }
                };
                _telemetryClient.TrackTrace("GetAllAttachments - Validation Error", SeverityLevel.Warning, properties);
                
                return BadRequest();
            }

            try
            {
                // List all attachments for this note
                var attachmentsList = await _storageService.ListAttachmentsAsync(noteId);

                if (attachmentsList == null)
                {
                    _logger.LogWarning("GetAllAttachments: container for note {NoteId} not found", noteId);
                    return NotFound(new { message = "Container not found" });
                }

                // Map to result objects
                var results = attachmentsList.Select(a => new AttachmentInfoResult
                {
                    AttachmentId = a.attachmentId,
                    ContentType = a.contentType,
                    CreatedDate = a.createdDate,
                    LastModifiedDate = a.lastModifiedDate,
                    Length = a.length
                }).ToList();

                _logger.LogInformation(
                    "GetAllAttachments: retrieved {Count} attachment(s) for note {NoteId}",
                    results.Count, noteId);

                return Ok(results);
            }
            catch (Exception ex)
            {
                // Track exception
                var properties = new Dictionary<string, string>
                {
                    { "ExceptionMessage", ex.Message },
                    { "InputPayload", JsonSerializer.Serialize(new { noteId }) }
                };
                _telemetryClient.TrackException(ex, properties);

                _logger.LogError(ex, "Error retrieving attachments for note {NoteId}", noteId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
