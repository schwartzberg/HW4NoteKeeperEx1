using System.ComponentModel.DataAnnotations;

namespace HW4NoteKeeperEx1.RequestAndResultObjects
{
    /// <summary>
    /// Request object for creating a new note.
    /// </summary>
    public class CreateNoteRequest
    {
        /// <summary>
        /// The summary of the note (1-60 characters, cannot be only whitespace).
        /// </summary>
        /// <example>hello world</example>
        [Required(ErrorMessage = "Summary is required")]
        [StringLength(60, MinimumLength = 1, ErrorMessage = "Summary must be between 1 and 60 characters")]
        [RegularExpression(@"^\S.*\S$|^\S$", ErrorMessage = "Summary cannot be only whitespace")]
        public string Summary { get; set; } = string.Empty;

        /// <summary>
        /// The detailed content of the note (1-1024 characters, cannot be only whitespace).
        /// </summary>
        /// <example>GitHub Copilot Chat is a companion extension to GitHub Copilot that allows you to chat with Copilot, an AI-powered assistant that helps you write better code. With GitHub Copilot Chat, you can access two key features: Chat View: Seek Copilot's assistance for any task or question within the Chat view. Inline Refinement: Apply Copilot's suggestions directly to your code, seamlessly maintaining your workflow.</example>
        [Required(ErrorMessage = "Details are required")]
        [StringLength(1024, MinimumLength = 1, ErrorMessage = "Details must be between 1 and 1024 characters")]
        [RegularExpression(@"^\S.*\S$|^\S$", ErrorMessage = "Details cannot be only whitespace")]
        public string Details { get; set; } = string.Empty;
    }
}
