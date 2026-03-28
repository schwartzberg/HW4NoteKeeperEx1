using HW4NoteKeeperEx1.Data;
using HW4NoteKeeperEx1.RequestAndResultObjects;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HW4NoteKeeperEx1.Controllers
{
    /// <summary>
    /// Controller for tag-related operations
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    [Produces("application/json")]
    public class TagsController : ControllerBase
    {
        private readonly MyDatabaseContext _context;
        private readonly ILogger<TagsController> _logger;

        private const string GetAllTagsRouteName = "GetAllTags";

        public TagsController(MyDatabaseContext context, ILogger<TagsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves a unique list of all tag names from the database.
        /// Returns an empty list if no tags exist.
        /// </summary>
        /// <returns>List of unique tag names</returns>
        [HttpGet(Name = GetAllTagsRouteName)]
        [ProducesResponseType(typeof(IEnumerable<TagNameResult>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<TagNameResult>>> GetAllTags()
        {
            try
            {
                var tagNames = await _context.Tags
                    .Select(t => t.Name)
                    .Distinct()
                    .OrderBy(name => name)
                    .Select(name => new TagNameResult { Name = name })
                    .ToListAsync();

                return Ok(tagNames);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all tags");
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
