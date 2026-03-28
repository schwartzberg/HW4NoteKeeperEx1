using System.Text.Json.Serialization;

namespace HW4AzureFunctionsEx1.Models
{
    /// <summary>
    /// Represents the JSON payload of a message in the <c>attachment-zip-requests</c> queue.
    /// </summary>
    public class ZipRequest
    {
        /// <summary>The ID (GUID string) of the note whose attachments should be zipped.</summary>
        [JsonPropertyName("noteId")]
        public string NoteId { get; set; } = string.Empty;

        /// <summary>The target blob name for the resulting zip file (e.g. "guid.zip").</summary>
        [JsonPropertyName("zipFileId")]
        public string ZipFileId { get; set; } = string.Empty;
    }
}
