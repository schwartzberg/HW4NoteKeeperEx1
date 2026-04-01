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
        private readonly JobsTableService _jobsTableService;
        private readonly TelemetryClient _telemetryClient;
        private readonly ILogger<NoteKeeperZipAttachmentControllerEx1> _logger;

        public NoteKeeperZipAttachmentControllerEx1(
            MyDatabaseContext context,
            JobsTableService jobsTableService,
            TelemetryClient telemetryClient,
            ILogger<NoteKeeperZipAttachmentControllerEx1> logger)
        {
            _context = context;
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
    }
}
