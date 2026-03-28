using Azure.Core;
using HW4NoteKeeperEx1.CustomSettings;
using HW4NoteKeeperEx1.Data;
using HW4NoteKeeperEx1.Models;
using HW4NoteKeeperEx1.RequestAndResultObjects;
using HW4NoteKeeperEx1.Services;
using HW4NoteKeeperEx1.Settings;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace HW4NoteKeeperEx1.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Produces("application/json")]
    [Consumes("application/json")]
    public class NoteKeeperController : ControllerBase
    {
        private readonly MyDatabaseContext _context;
        private readonly IChatClient _chatClient;
        private readonly TagGeneratorService _tagGeneratorService;
        private readonly ILogger<NoteKeeperController> _logger;
        private readonly NoteLimits _noteLimits;
        private readonly TelemetryClient _telemetryClient;

        private const string GetNoteRouteName = "GetNoteById";
        private const string GetNotesRouteName = "GetNotes";
        private const string CreateNoteRouteName = "CreateNote";
        private const string UpdateNoteRouteName = "UpdateNote";
        private const string DeleteNoteRouteName = "DeleteNote";
         
        public NoteKeeperController(MyDatabaseContext context,
                                     ILogger<NoteKeeperController> logger,
                                     AISettings aISettings,
                                     IChatClient chatClient,
                                     NoteLimits noteLimits,
                                     TelemetryClient telemetryClient)
        {
            _context = context;
            _chatClient = chatClient;
            _tagGeneratorService = new TagGeneratorService(chatClient, aISettings, logger);
            _logger = logger;
            _noteLimits = noteLimits;
            _telemetryClient = telemetryClient;
        }

        /// <summary>
        /// Retrieves all notes currently stored, optionally filtered by tag name.
        /// Returned list may be empty if no notes exist or match the filter.
        /// </summary>
        /// <param name="tagName">Optional tag name to filter notes by</param>
        /// <returns>All matching notes in the system</returns>
        [HttpGet(Name = GetNotesRouteName)]
        [ProducesResponseType(typeof(IEnumerable<NoteResult>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<NoteResult>>> Get([FromQuery] string? tagName = null)
        {
            IQueryable<Note> query = _context.Notes;

            if (!string.IsNullOrWhiteSpace(tagName))
            {
                query = query.Where(n => n.Tags.Any(t => t.Name == tagName));
            }

            var notes = await query
                .Select(n => new NoteResult
                {
                    Id = n.Id,
                    Summary = n.Summary,
                    Details = n.Details,
                    CreatedDateUtc = n.CreatedDateUtc,
                    ModifiedDateUtc = n.ModifiedDateUtc,
                    Tags = n.Tags.Select(t => t.Name).ToList()
                })
                .ToListAsync();

            _telemetryClient.TrackEvent("All Notes retrieved",
                            properties: new Dictionary<string, string>()
                            {
                                            { "Count", notes.Count.ToString() }
                            });

            return Ok(notes);
        }

        /// <summary>
        /// Creates a new note entry and generates tags using GPT-5-mini
        /// </summary>
        /// <param name="request">The note to create with summary and details</param>
        /// <returns>The created note with generated tags</returns>
        [HttpPost(Name = CreateNoteRouteName)]
        [ProducesResponseType(typeof(NoteResult), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<NoteResult>> Post([FromBody] CreateNoteRequest request)
        {   
            if (!ModelState.IsValid)
            {
                var validationErrors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray()
                    );
                
                IDictionary<string, string> properties = new Dictionary<string, string>();
                properties.Add("Details of the validation error", JsonSerializer.Serialize(validationErrors));
                properties.Add("InputPayload", JsonSerializer.Serialize(request));
                _telemetryClient.TrackTrace("CreateNote - Validation Error", SeverityLevel.Warning, properties);
                return BadRequest();
            }

            // Check if adding a note would exceed the MaxNotes limit
            int currentCount = await _context.Notes.CountAsync();
            if (currentCount >= _noteLimits.MaxNotes)
            {
                return Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Note limit reached",
                    detail: $"Note limit reached MaxNotes: [{_noteLimits.MaxNotes}]"
                );
            }

            try
            {
                // Generate tags using GPT-5-mini
                KeyTagsResponse? tagResponse = await _tagGeneratorService.GenerateTags(request.Details);

                var noteId = Guid.NewGuid();
                var note = new Note
                {
                    Id = noteId,
                    Summary = request.Summary.Trim(),
                    Details = request.Details.Trim(),
                    CreatedDateUtc = DateTimeOffset.UtcNow,
                    ModifiedDateUtc = null
                };

                _context.Notes.Add(note);

                // Add tags
                if (tagResponse.Tags != null && tagResponse.Tags.Count > 0)
                {
                    foreach (var tagName in tagResponse.Tags)
                    {
                        // Truncate tag name if longer than 30 characters
                        string truncatedTagName = tagName.Length > 30 ? tagName.Substring(0, 30) : tagName;

                        var tag = new Tag
                        {
                            Id = Guid.NewGuid(),
                            NoteId = noteId,
                            Name = truncatedTagName
                        };
                        _context.Tags.Add(tag);
                    }
                }


                await _context.SaveChangesAsync();

                // Create result object
                var result = new NoteResult
                {
                    Id = note.Id,
                    Summary = note.Summary,
                    Details = note.Details,
                    CreatedDateUtc = note.CreatedDateUtc,
                    ModifiedDateUtc = note.ModifiedDateUtc,
                    Tags = tagResponse.Tags ?? new List<string>()
                };

                _telemetryClient.TrackEvent("NoteCreated",
                                       properties: new Dictionary<string, string>()
                                       {
                                            { "named summary", note.Summary },
                                            { "tagcount", result.Tags.Count.ToString() }
                                       },
                                       metrics: new Dictionary<string, double>()
                                       {
                                            { "SummaryLength", note.Summary.Length },
                                            { "DetailsLength", note.Details.Length }
                                       });

                return CreatedAtRoute(GetNoteRouteName, routeValues: new { noteId = noteId }, value: result);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex,
                                       properties: new Dictionary<string, string>()
                                       {
                                            { "Exception message", ex.Message },
                                            { "InputPayload", JsonSerializer.Serialize(request) }
                                       });

                _logger.LogError(ex, "Error creating note with summary: {Summary}", request.Summary);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Updates an existing note. Summary and Details are both optional.
        /// Tags are regenerated only if Details are changed.
        /// </summary>
        /// <param name="noteId">The ID of the note to update</param>
        /// <param name="request">The updated summary and/or details</param>
        /// <returns>No content on success</returns>
        [HttpPatch("{noteId}", Name = UpdateNoteRouteName)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status405MethodNotAllowed)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Patch(string noteId, [FromBody] UpdateNoteRequest request)
        {     
            if (string.IsNullOrWhiteSpace(noteId))
            {
                IDictionary<string, string> properties = new Dictionary<string, string>();
                properties.Add("Details of the validation error", "NoteId is null or whitespace");
                properties.Add("InputPayload", JsonSerializer.Serialize(new { NoteId = noteId, Request = request }));
                _telemetryClient.TrackTrace("UpdateNote - Validation Error", SeverityLevel.Warning, properties);
                return StatusCode(StatusCodes.Status405MethodNotAllowed);
            }

            // Try to parse the GUID
            if (!Guid.TryParse(noteId, out Guid guidNoteId))
            {
                IDictionary<string, string> properties = new Dictionary<string, string>();
                properties.Add("Details of the validation error", "Invalid GUID format for noteId");
                properties.Add("InputPayload", JsonSerializer.Serialize(new { NoteId = noteId, Request = request }));
                _telemetryClient.TrackTrace("UpdateNote - Validation Error", SeverityLevel.Warning, properties);
                return BadRequest();
            }

            var summaryIsEmpty = string.IsNullOrWhiteSpace(request?.Summary);
            var detailsIsEmpty = string.IsNullOrWhiteSpace(request?.Details);
            var countNewTags = 0;

            try
            {
                var existingNote = await _context.Notes
                    .Include(n => n.Tags)
                    .FirstOrDefaultAsync(n => n.Id == guidNoteId);

                if (existingNote == null)
                {
                    return NotFound();
                }

                bool summaryChanged = false;
                bool detailsChanged = false;

                if (!summaryIsEmpty)
                {
                    existingNote.Summary = request!.Summary!.Trim();
                    summaryChanged = true;
                }

                //We update the tags only if request?.Details is filled and not equal the existing.
                if (!detailsIsEmpty && existingNote.Details.Trim() != request!.Details!.Trim())
                {
                    existingNote.Details = request!.Details!.Trim();
                    detailsChanged = true;

                    // Regenerate tags
                    KeyTagsResponse? tagResponse = await _tagGeneratorService.GenerateTags(existingNote.Details);

                    // Remove old tags
                    _context.Tags.RemoveRange(existingNote.Tags);

                    // Add new tags
                    if (tagResponse.Tags != null && tagResponse.Tags.Count > 0)
                    {
                        foreach (var tagName in tagResponse.Tags)
                        {
                            string truncatedTagName = tagName.Length > 30 ? tagName.Substring(0, 30) : tagName;

                            var tag = new Tag
                            {
                                Id = Guid.NewGuid(),
                                NoteId = existingNote.Id,
                                Name = truncatedTagName
                            };
                            _context.Tags.Add(tag);
                            countNewTags++;
                        }
                    }
                }

                if (summaryChanged || detailsChanged)
                {
                    existingNote.ModifiedDateUtc = DateTimeOffset.UtcNow;

                    // Prepare properties and metrics for TrackEvent based on what was actually updated
                    string namedSummary = summaryChanged ? existingNote.Summary : "";
                    string namedDetails = detailsChanged ? existingNote.Details : "";
                    double summaryLengthMetric = summaryChanged ? existingNote.Summary.Length : 0;
                    double detailsLengthMetric = detailsChanged ? existingNote.Details.Length : 0;
                    double tagCountMetric = detailsChanged ? countNewTags : 0;

                    _telemetryClient.TrackEvent("NoteUpdated",
                                    properties: new Dictionary<string, string>()
                                    { 
                                            { "named summary", namedSummary },
                                            { "named details", namedDetails }
                                    },
                                    metrics: new Dictionary<string, double>()
                                    {
                                            { "SummaryLength", summaryLengthMetric },
                                            { "DetailsLength", detailsLengthMetric },
                                            { "TagCount", tagCountMetric }
                                    });

                    await _context.SaveChangesAsync();
                } 
               
                return NoContent();
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex,
                                      properties: new Dictionary<string, string>()
                                      {
                                           { "Exception with update of node id message", noteId }, 
                                           { "Exception message", ex.Message },
                                           { "InputPayload", JsonSerializer.Serialize(new { NoteId = noteId, Request = request }) }
                                      });

                _logger.LogError(ex, "Error updating note with ID: {NoteId}", noteId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Deletes an existing note by its ID
        /// </summary>
        /// <param name="noteId">The ID of the note to delete</param>
        /// <returns>No content on success</returns>
        [HttpDelete("{noteId}", Name = DeleteNoteRouteName)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status405MethodNotAllowed)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Delete(string noteId)
        {
            if (string.IsNullOrWhiteSpace(noteId))
            {
                IDictionary<string, string> properties = new Dictionary<string, string>();
                properties.Add("Details of the validation error", "NoteId is null or whitespace");
                properties.Add("InputPayload", noteId ?? "null");
                _telemetryClient.TrackTrace("DeleteNote - Validation Error", SeverityLevel.Warning, properties);
                return StatusCode(StatusCodes.Status405MethodNotAllowed);
            }

            // Try to parse the GUID
            if (!Guid.TryParse(noteId, out Guid guidNoteId))
            {
                IDictionary<string, string> properties = new Dictionary<string, string>();
                properties.Add("Details of the validation error", "Invalid GUID format for noteId");
                properties.Add("InputPayload", noteId);
                _telemetryClient.TrackTrace("DeleteNote - Validation Error", SeverityLevel.Warning, properties);
                return BadRequest();
            }

            try
            {
                var existingNote = await _context.Notes
                    .Include(n => n.Tags)
                    .FirstOrDefaultAsync(n => n.Id == guidNoteId);

                if (existingNote == null)
                {
                    return NotFound();
                }

                // EF Core will cascade delete the tags automatically
                _context.Notes.Remove(existingNote);
                await _context.SaveChangesAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex,
                                      properties: new Dictionary<string, string>()
                                      {
                                           { "Exception message", ex.Message },
                                           { "InputPayload", noteId }
                                      });

                _logger.LogError(ex, "Error deleting note with ID: {NoteId}", noteId);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Retrieves a note having the specified noteId
        /// </summary>
        /// <param name="noteId">The ID of the note to retrieve</param>
        /// <returns>The note details if found</returns>
        [HttpGet("{noteId}", Name = GetNoteRouteName)]
        [ProducesResponseType(typeof(NoteResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById(string noteId)
        {
            if (string.IsNullOrWhiteSpace(noteId))
            {
                IDictionary<string, string> properties = new Dictionary<string, string>();
                properties.Add("Details of the validation error", "NoteId is null or whitespace");
                properties.Add("InputPayload", noteId ?? "null");
                _telemetryClient.TrackTrace("GetNoteById - Validation Error", SeverityLevel.Warning, properties);
                return BadRequest();
            }

            // Try to parse the GUID
            if (!Guid.TryParse(noteId, out Guid guidNoteId))
            {
                IDictionary<string, string> properties = new Dictionary<string, string>();
                properties.Add("Details of the validation error", "Invalid GUID format for noteId");
                properties.Add("InputPayload", noteId);
                _telemetryClient.TrackTrace("GetNoteById - Validation Error", SeverityLevel.Warning, properties);
                return BadRequest();
            }

            try
            {
                var note = await _context.Notes
                    .AsNoTracking()
                    .Include(n => n.Tags)
                    .Where(n => n.Id == guidNoteId)
                    .Select(n => new NoteResult
                    {
                        Id = n.Id,
                        Summary = n.Summary,
                        Details = n.Details,
                        CreatedDateUtc = n.CreatedDateUtc,
                        ModifiedDateUtc = n.ModifiedDateUtc,
                        Tags = n.Tags.Select(t => t.Name).ToList()
                    })
                    .FirstOrDefaultAsync();

                if (note == null)
                {
                    return NotFound();
                }


                _telemetryClient.TrackEvent("A note is retrieved",
                          properties: new Dictionary<string, string>()
                          { 
                                            {"note summary",note.Summary }
                          });

                return Ok(note);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving note with ID: {NoteId}", noteId);
                _telemetryClient.TrackException(ex,
                          properties: new Dictionary<string, string>()
                          {   
                                            { "Exception message", ex.Message },
                                            { "InputPayload", noteId }
                          });

                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        } 

    }
}
