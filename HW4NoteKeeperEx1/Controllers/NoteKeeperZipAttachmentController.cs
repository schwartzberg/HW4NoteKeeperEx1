using HW4NoteKeeperEx1.Data;
using HW4NoteKeeperEx1.RequestAndResultObjects;
using HW4NoteKeeperEx1.Services;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HW4NoteKeeperEx1.Controllers
{
    /// <summary>
    /// Manages zip archive files for note attachments.
    /// Zip archives are created asynchronously via an Azure Storage Queue; this controller
    /// enqueues requests, lists/downloads/deletes completed zip files, and provides an
    /// enhanced note-delete that removes all associated storage artefacts and database rows.
    /// </summary>
    /// <remarks>
    /// Base route: <c>notes/{noteId}</c>
    /// Zip-file sub-route: <c>notes/{noteId}/attachmentzipfiles</c>
    /// Enhanced note delete: <c>DELETE notes/{noteId}</c>
    /// </remarks>
    [ApiController]
    [Route("notes/{noteId}")]
    [Produces("application/json")]
    public class NoteKeeperZipAttachmentController : ControllerBase
    {
        private readonly MyDatabaseContext _context;
        private readonly AzureStorageService _storageService;
        private readonly JobsTableService _jobsTableService;
        private readonly TelemetryClient _telemetryClient;
        private readonly ILogger<NoteKeeperZipAttachmentController> _logger;

        /// <summary>
        /// Initializes a new instance of <see cref="NoteKeeperZipAttachmentController"/>.
        /// </summary>
        public NoteKeeperZipAttachmentController(
            MyDatabaseContext context,
            AzureStorageService storageService,
            JobsTableService jobsTableService,
            TelemetryClient telemetryClient,
            ILogger<NoteKeeperZipAttachmentController> logger)
        {
            _context = context;
            _storageService = storageService;
            _jobsTableService = jobsTableService;
            _telemetryClient = telemetryClient;
            _logger = logger;
        }

        // ─── POST notes/{noteId}/attachmentzipfiles ────────────────────────────────

        /// <summary>
        /// Requests creation of a zip archive containing all current attachments for the note.
        /// Enqueues a message to the <c>attachment-zip-requests</c> queue; the zip file is
        /// created asynchronously by the <c>HW4AzureFunctionsEx1</c> Azure Function.
        /// </summary>
        /// <param name="noteId">The GUID of the note whose attachments should be zipped.</param>
        /// <returns>
        /// 202 Accepted with a <c>Location</c> header pointing to the future zip file;
        /// 204 No Content if the note has no attachments;
        /// 400 Bad Request if <paramref name="noteId"/> is not a valid GUID;
        /// 404 Not Found if the note does not exist in the database.
        /// </returns>
        [HttpPost("attachmentzipfiles")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> RequestZipCreation(string noteId)
        {
            if (!IsValidNoteId(noteId, out Guid _))
            {
                _logger.LogWarning("RequestZipCreation: invalid noteId '{NoteId}'", noteId);
                TrackValidationError("RequestZipCreation", "Invalid GUID format for noteId", noteId);
                return BadRequest();
            }

            try
            {
                // 1.1.3 – Note must exist in the database
                bool noteExists = await _context.Notes.AnyAsync(n => n.Id == Guid.Parse(noteId));
                if (!noteExists)
                {
                    _logger.LogWarning("RequestZipCreation: note {NoteId} not found", noteId);
                    return NotFound();
                }

                // 1.1.5 – Note must have at least one attachment
                int attachmentCount = await _storageService.GetBlobCountAsync(noteId);
                if (attachmentCount == 0)
                {
                    _logger.LogInformation("RequestZipCreation: note {NoteId} has no attachments – returning 204", noteId);
                    return NoContent();
                }

                // Generate the target zip file name
                string zipFileId = $"{Guid.NewGuid()}.zip";

                // §2.3.1 – Insert a Queued row into the Jobs table BEFORE enqueuing
                // so the Azure Function always finds the row when it starts processing
                await _jobsTableService.InsertQueuedJobAsync(noteId, zipFileId);

                // Enqueue the zip request
                await _storageService.EnqueueZipRequestAsync(noteId, zipFileId);

                _telemetryClient.TrackEvent("ZipRequested",
                    new Dictionary<string, string> { { "noteId", noteId }, { "zipFileId", zipFileId } });

                string locationUrl = $"{Request.Scheme}://{Request.Host}/notes/{noteId}/attachmentzipfiles/{zipFileId}";
                return Accepted(locationUrl, (object?)null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting zip creation for note {NoteId}", noteId);
                TrackException(ex, "RequestZipCreation", noteId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        // ─── DELETE notes/{noteId}/attachmentzipfiles/{zipFileId} ─────────────────

        /// <summary>
        /// Deletes a specific zip blob from the note's zip container.
        /// </summary>
        /// <param name="noteId">The GUID of the note.</param>
        /// <param name="zipFileId">The zip file name to delete (e.g. "guid.zip").</param>
        /// <returns>
        /// 204 No Content whether or not the zip file existed;
        /// 400 Bad Request if any parameter is invalid;
        /// 404 Not Found if the note does not exist in the database;
        /// 500 Internal Server Error if an unexpected error occurs.
        /// </returns>
        [HttpDelete("attachmentzipfiles/{zipFileId}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteZipFile(string noteId, string zipFileId)
        {
            if (!IsValidNoteId(noteId, out Guid _))
            {
                TrackValidationError("DeleteZipFile", "Invalid GUID format for noteId", noteId);
                return BadRequest();
            }

            if (string.IsNullOrWhiteSpace(zipFileId))
            {
                TrackValidationError("DeleteZipFile", "zipFileId is null or empty", noteId);
                return BadRequest();
            }

            try
            {
                bool noteExists = await _context.Notes.AnyAsync(n => n.Id == Guid.Parse(noteId));
                if (!noteExists)
                {
                    _logger.LogWarning("DeleteZipFile: note {NoteId} not found", noteId);
                    return NotFound();
                }

                AttachmentDeleteResult result = await _storageService.DeleteZipBlobAsync(noteId, zipFileId);

                if (result == AttachmentDeleteResult.Error)
                {
                    _logger.LogError("DeleteZipFile: failed to delete zip blob {ZipFileId} for note {NoteId}", zipFileId, noteId);
                    return StatusCode(StatusCodes.Status500InternalServerError);
                }

                _telemetryClient.TrackEvent("ZipDeleted",
                    new Dictionary<string, string> { { "noteId", noteId }, { "zipFileId", zipFileId } });

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting zip blob {ZipFileId} for note {NoteId}", zipFileId, noteId);
                TrackException(ex, "DeleteZipFile", noteId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        // ─── GET notes/{noteId}/attachmentzipfiles/{zipFileId} ────────────────────

        /// <summary>
        /// Downloads a specific zip file from the note's zip container.
        /// </summary>
        /// <param name="noteId">The GUID of the note.</param>
        /// <param name="zipFileId">The zip file name to download (e.g. "guid.zip").</param>
        /// <returns>
        /// 200 OK with the zip file stream (content-type <c>application/zip</c>);
        /// 400 Bad Request if any parameter is invalid;
        /// 404 Not Found if the note or zip file does not exist;
        /// 500 Internal Server Error if an unexpected error occurs.
        /// </returns>
        [HttpGet("attachmentzipfiles/{zipFileId}")]
        [Produces("application/zip")]
        [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetZipFile(string noteId, string zipFileId)
        {
            if (!IsValidNoteId(noteId, out Guid _))
            {
                TrackValidationError("GetZipFile", "Invalid GUID format for noteId", noteId);
                return BadRequest();
            }

            if (string.IsNullOrWhiteSpace(zipFileId))
            {
                TrackValidationError("GetZipFile", "zipFileId is null or empty", noteId);
                return BadRequest();
            }

            try
            {
                bool noteExists = await _context.Notes.AnyAsync(n => n.Id == Guid.Parse(noteId));
                if (!noteExists)
                {
                    _logger.LogWarning("GetZipFile: note {NoteId} not found", noteId);
                    return NotFound();
                }

                var result = await _storageService.DownloadZipBlobAsync(noteId, zipFileId);
                if (result is null)
                {
                    _logger.LogWarning("GetZipFile: zip blob {ZipFileId} not found for note {NoteId}", zipFileId, noteId);
                    return NotFound();
                }

                _telemetryClient.TrackEvent("ZipDownloaded",
                    new Dictionary<string, string> { { "noteId", noteId }, { "zipFileId", zipFileId } });

                return File(result.Value.stream, "application/zip", zipFileId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading zip blob {ZipFileId} for note {NoteId}", zipFileId, noteId);
                TrackException(ex, "GetZipFile", noteId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        // ─── GET notes/{noteId}/attachmentzipfiles ────────────────────────────────

        /// <summary>
        /// Lists all zip files available for the note.
        /// </summary>
        /// <param name="noteId">The GUID of the note.</param>
        /// <returns>
        /// 200 OK with an array of <see cref="ZipBlobInfo"/> (empty array if no zip files exist);
        /// 400 Bad Request if <paramref name="noteId"/> is not a valid GUID;
        /// 404 Not Found if the note does not exist in the database;
        /// 500 Internal Server Error if an unexpected error occurs.
        /// </returns>
        [HttpGet("attachmentzipfiles")]
        [ProducesResponseType(typeof(IEnumerable<ZipBlobInfo>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAllZipFiles(string noteId)
        {
            if (!IsValidNoteId(noteId, out Guid _))
            {
                TrackValidationError("GetAllZipFiles", "Invalid GUID format for noteId", noteId);
                return BadRequest();
            }

            try
            {
                bool noteExists = await _context.Notes.AnyAsync(n => n.Id == Guid.Parse(noteId));
                if (!noteExists)
                {
                    _logger.LogWarning("GetAllZipFiles: note {NoteId} not found", noteId);
                    return NotFound();
                }

                var blobs = await _storageService.ListZipBlobsAsync(noteId);

                // Container not yet created (no zips ever made) → return empty list
                if (blobs is null)
                    return Ok(Array.Empty<ZipBlobInfo>());

                var zipBlobInfoList = blobs.Select(b => new ZipBlobInfo
                {
                    ZipFileId = b.zipFileId,
                    ContentType = b.contentType,
                    CreatedDate = b.createdDate,
                    LastModifiedDate = b.lastModifiedDate,
                    Length = b.length
                }).ToList();

                return Ok(zipBlobInfoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing zip blobs for note {NoteId}", noteId);
                TrackException(ex, "GetAllZipFiles", noteId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        // ─── DELETE notes/{noteId} – Enhanced note delete (1.5) ──────────────────

    /// <summary>
    /// Permanently deletes the note and all associated data:
    /// the note record (with cascade-deleted tags) from the database,
    /// the attachment blob container (named with the note ID) and all its blobs,
    /// the zip blob container (named "{noteId}-zip") and all its blobs,
    /// and all job tracking rows in the Azure Storage Jobs table.
    /// </summary>
    /// <param name="noteId">The GUID of the note to delete.</param>
    /// <returns>
    /// 204 No Content on success;
    /// 400 Bad Request if <paramref name="noteId"/> is not a valid GUID;
    /// 404 Not Found if the note does not exist in the database;
    /// 409 Conflict if any zip-creation job for this note is currently InProgress (§4.1.4);
    /// 500 Internal Server Error if an unexpected error occurs.
    /// </returns>
        [HttpDelete]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteNoteWithAllAssets(string noteId)
        {
            if (string.IsNullOrWhiteSpace(noteId))
            {
                TrackValidationError("DeleteNoteWithAllAssets", "noteId is null or whitespace", noteId ?? "null");
                return BadRequest();
            }

            if (!IsValidNoteId(noteId, out Guid guidNoteId))
            {
                TrackValidationError("DeleteNoteWithAllAssets", "Invalid GUID format for noteId", noteId);
                return BadRequest();
            }

            try
            {
                var existingNote = await _context.Notes
                    .Include(n => n.Tags)
                    .FirstOrDefaultAsync(n => n.Id == guidNoteId);

                if (existingNote is null)
                {
                    _logger.LogWarning("DeleteNoteWithAllAssets: note {NoteId} not found", noteId);
                    return NotFound();
                }

                // §4.1.4 – If any job is InProgress, fail with 409 Conflict
                try
                {
                    bool hasInProgress = await _jobsTableService.HasInProgressJobsAsync(noteId);
                    if (hasInProgress)
                    {
                        _logger.LogWarning(
                            "DeleteNoteWithAllAssets: note {NoteId} has InProgress compression job(s) – returning 409 Conflict",
                            noteId);
                        return Conflict();
                    }
                }
                catch (Exception ex)
                {
                    // Cannot verify InProgress state — conservatively return 409
                    _logger.LogError(ex,
                        "DeleteNoteWithAllAssets: error checking InProgress jobs for note {NoteId} – returning 409 Conflict (cannot confirm safe to delete)",
                        noteId);
                    return Conflict();
                }

                // §4.1 – Delete all job rows for this noteId from the Jobs table
                try
                {
                    int deletedJobCount = await _jobsTableService.DeleteJobsByNoteIdAsync(noteId);

                    // §4.1.2 – Log info if no rows existed
                    if (deletedJobCount == 0)
                    {
                        _logger.LogInformation(
                            "DeleteNoteWithAllAssets: no job rows found in Jobs table for note {NoteId}", noteId);
                    }
                }
                catch (Exception ex)
                {
                    // §4.1.3 – Log error but do not fail the request
                    _logger.LogError(ex,
                        "DeleteNoteWithAllAssets: error deleting job rows for note {NoteId} from Jobs table – continuing",
                        noteId);
                }

                // Delete attachment container (and all blobs) – idempotent if it doesn't exist
                await _storageService.DeleteContainerIfExistsAsync(noteId);

                // Delete zip container (and all zip blobs) – idempotent
                await _storageService.DeleteContainerIfExistsAsync($"{noteId}-zip");

                // Delete note (tags cascade-deleted by EF Core / database)
                _context.Notes.Remove(existingNote);
                await _context.SaveChangesAsync();

                _telemetryClient.TrackEvent("NoteDeletedWithAllAssets",
                    new Dictionary<string, string> { { "noteId", noteId } });

                _logger.LogInformation("Deleted note {NoteId} with all storage assets and job rows", noteId);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting note {NoteId} with all assets", noteId);
                TrackException(ex, "DeleteNoteWithAllAssets", noteId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        // ─── Private helpers ──────────────────────────────────────────────────────

        private static bool IsValidNoteId(string noteId, out Guid guidNoteId)
            => Guid.TryParse(noteId, out guidNoteId);

        private void TrackValidationError(string operation, string error, string input)
        {
            _telemetryClient.TrackTrace(
                $"{operation} - Validation Error",
                SeverityLevel.Warning,
                new Dictionary<string, string>
                {
                    { "ValidationError", error },
                    { "InputPayload", input }
                });
        }

        private void TrackException(Exception ex, string operation, string noteId)
        {
            _telemetryClient.TrackException(ex,
                new Dictionary<string, string>
                {
                    { "Operation", operation },
                    { "NoteId", noteId },
                    { "ExceptionMessage", ex.Message }
                });
        }
    }
}
