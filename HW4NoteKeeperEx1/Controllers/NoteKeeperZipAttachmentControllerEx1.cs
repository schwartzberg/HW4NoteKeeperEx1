using HW4NoteKeeperEx1.Data;
using HW4NoteKeeperEx1.RequestAndResultObjects;
using HW4NoteKeeperEx1.Services;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HW4NoteKeeperEx1.Controllers
{
    /// <summary>
    /// Extra Credit 1 controller providing GET endpoints for job status tracking.
    /// Routes: notes/{noteId}/attachmentzipfiles/jobs[/{zipFileId}]
    /// </summary>
    [ApiController]
    [Route("notes/{noteId}/attachmentzipfiles/jobs")]
    [Produces("application/json")]
    public class NoteKeeperZipAttachmentControllerEx1 : ControllerBase
    {
        private readonly MyDatabaseContext _context;
        private readonly AzureStorageService _storageService;
        private readonly JobsTableService _jobsTableService;
        private readonly TelemetryClient _telemetryClient;
        private readonly ILogger<NoteKeeperZipAttachmentControllerEx1> _logger;

        public NoteKeeperZipAttachmentControllerEx1(
            MyDatabaseContext context,
            AzureStorageService storageService,
            JobsTableService jobsTableService,
            TelemetryClient telemetryClient,
            ILogger<NoteKeeperZipAttachmentControllerEx1> logger)
        {
            _context = context;
            _storageService = storageService;
            _jobsTableService = jobsTableService;
            _telemetryClient = telemetryClient;
            _logger = logger;
        }

        /// <summary>
        /// §2.5 – Returns the job status for a specific zip file associated with a note.
        /// </summary>
        /// <param name="noteId">The note ID (GUID).</param>
        /// <param name="zipFileId">The zip file ID (e.g. "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx.zip").</param>
        /// <returns>
        /// 200 OK with <see cref="JobStatusResponse"/> if found;
        /// 400 Bad Request if noteId is not a valid GUID;
        /// 404 Not Found if the note does not exist or no job row matches.
        /// </returns>
        [HttpGet("{zipFileId}")]
        [ProducesResponseType(typeof(JobStatusResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetJobStatus(string noteId, string zipFileId)
        {
            if (!Guid.TryParse(noteId, out Guid guidNoteId))
            {
                _logger.LogWarning("GetJobStatus: invalid GUID format for noteId={NoteId}", noteId);
                return BadRequest();
            }

            try
            {
                // Verify the note exists in the database
                bool noteExists = await _context.Notes.AnyAsync(n => n.Id == guidNoteId);
                if (!noteExists)
                {
                    _logger.LogWarning("GetJobStatus: note {NoteId} not found in database", noteId);
                    return NotFound();
                }

                var job = await _jobsTableService.GetJobAsync(noteId, zipFileId);
                if (job is null)
                {
                    _logger.LogWarning(
                        "GetJobStatus: no job row found for NoteId={NoteId}, ZipFileId={ZipFileId}",
                        noteId, zipFileId);
                    return NotFound();
                }

                var response = new JobStatusResponse
                {
                    ZipFileId = job.RowKey ?? string.Empty,
                    TimeStamp = job.Timestamp ?? DateTimeOffset.UtcNow,
                    Status = job.Status ?? string.Empty,
                    StatusDetails = job.StatusDetails ?? string.Empty
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving job status for NoteId={NoteId}, ZipFileId={ZipFileId}",
                    noteId, zipFileId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// §3 – Returns all job statuses for the specified note.
        /// </summary>
        /// <param name="noteId">The note ID (GUID).</param>
        /// <returns>
        /// 200 OK with a list of <see cref="JobStatusResponse"/> if found;
        /// 400 Bad Request if noteId is not a valid GUID;
        /// 404 Not Found if the note does not exist.
        /// </returns>
        [HttpGet]
        [ProducesResponseType(typeof(List<JobStatusResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAllJobStatuses(string noteId)
        {
            if (!Guid.TryParse(noteId, out Guid guidNoteId))
            {
                _logger.LogWarning("GetAllJobStatuses: invalid GUID format for noteId={NoteId}", noteId);
                return BadRequest();
            }

            try
            {
                // Verify the note exists in the database
                bool noteExists = await _context.Notes.AnyAsync(n => n.Id == guidNoteId);
                if (!noteExists)
                {
                    _logger.LogWarning("GetAllJobStatuses: note {NoteId} not found in database", noteId);
                    return NotFound();
                }

                var jobs = await _jobsTableService.GetJobsByNoteIdAsync(noteId);

                var response = jobs.Select(job => new JobStatusResponse
                {
                    ZipFileId = job.RowKey ?? string.Empty,
                    TimeStamp = job.Timestamp ?? DateTimeOffset.UtcNow,
                    Status = job.Status ?? string.Empty,
                    StatusDetails = job.StatusDetails ?? string.Empty
                }).ToList();

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all job statuses for NoteId={NoteId}", noteId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
        /// <summary>
        /// §Ex1 POST – Requests creation of a zip archive for the note's attachments using the Ex1
        /// flow with job-status tracking. Enqueues a message to <c>attachment-zip-requests-ex1</c>
        /// and inserts a <c>Queued</c> row into the Jobs table before returning.
        /// </summary>
        /// <param name="noteId">The GUID of the note whose attachments should be zipped.</param>
        /// <returns>
        /// 202 Accepted with a <c>Location</c> header pointing to the job-status endpoint;
        /// 204 No Content if the note has no attachments;
        /// 400 Bad Request if <paramref name="noteId"/> is not a valid GUID;
        /// 404 Not Found if the note does not exist in the database.
        /// </returns>
        [HttpPost("/notes/{noteId}/attachmentzipfilesex1")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> RequestZipCreationEx1(string noteId)
        {
            if (!Guid.TryParse(noteId, out Guid guidNoteId))
            {
                _logger.LogWarning("RequestZipCreationEx1: invalid noteId '{NoteId}'", noteId);
                return BadRequest();
            }

            try
            {
                bool noteExists = await _context.Notes.AnyAsync(n => n.Id == guidNoteId);
                if (!noteExists)
                {
                    _logger.LogWarning("RequestZipCreationEx1: note {NoteId} not found", noteId);
                    return NotFound();
                }

                int attachmentCount = await _storageService.GetBlobCountAsync(noteId);
                if (attachmentCount == 0)
                {
                    _logger.LogInformation("RequestZipCreationEx1: note {NoteId} has no attachments – returning 204", noteId);
                    return NoContent();
                }

                string zipFileId = $"{Guid.NewGuid()}.zip";

                // Insert Queued row BEFORE enqueuing so the function always finds the row
                await _jobsTableService.InsertQueuedJobAsync(noteId, zipFileId);

                // Enqueue to the Ex1 queue (attachment-zip-requests-ex1)
                await _storageService.EnqueueZipRequestAsync(noteId, zipFileId);

                _telemetryClient.TrackEvent("ZipRequestedEx1",
                    new Dictionary<string, string> { { "noteId", noteId }, { "zipFileId", zipFileId } });

                // Location points to the job-status endpoint so clients can poll for progress
                string locationUrl = $"{Request.Scheme}://{Request.Host}/notes/{noteId}/attachmentzipfiles/jobs/{zipFileId}";
                return Accepted(locationUrl, (object?)null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting Ex1 zip creation for note {NoteId}", noteId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
