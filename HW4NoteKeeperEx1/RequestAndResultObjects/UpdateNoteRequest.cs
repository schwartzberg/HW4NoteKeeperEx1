using System.ComponentModel.DataAnnotations;

namespace HW4NoteKeeperEx1.RequestAndResultObjects
{
    /// <summary>
    /// Represents a request to update an existing note.
    /// At least one of Summary or Details must be provided.
    /// </summary>
    public class UpdateNoteRequest
    {
        /// <summary>
        /// Gets or sets the updated summary of the note (1-60 characters).
        /// Optional - provide only if updating the summary.
        /// </summary>
        /// <example>hello world</example>
        [StringLength(60, MinimumLength = 1)]
        [RegularExpression(@"^\S.*\S$|^\S$", ErrorMessage = "Summary cannot be only whitespace")]
        public string? Summary { get; set; }

        /// <summary>
        /// Gets or sets the updated detailed content of the note (1-1024 characters).
        /// Optional - provide only if updating the details. When updated, tags will be regenerated.
        /// </summary>
        /// <example>GitHub Copilot Chat is a companion extension to GitHub Copilot that allows you to chat with Copilot, an AI-powered assistant that helps you to write better code. With GitHub Copilot Chat, you can access two key features: Chat View: Seek Copilot's assistance for any task or question within the Chat view. Inline Refinement: Apply Copilot's suggestions directly to your code, seamlessly maintaining your workflow.</example>
        [StringLength(1024, MinimumLength = 1)]
        [RegularExpression(@"^\S.*\S$|^\S$", ErrorMessage = "Details cannot be only whitespace")]
        public string? Details { get; set; }
    }
}
