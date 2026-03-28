namespace HW4NoteKeeperEx1.RequestAndResultObjects
{
    /// <summary>
    /// Represents a note result returned by the API with flattened tag information.
    /// </summary>
    public class NoteResult
    {
        /// <summary>
        /// Gets or sets the unique identifier for the note.
        /// Serialized as "noteId" in JSON per API spec.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("noteId")]
        public Guid Id { get; set; }

        /// <summary>
        /// Gets or sets the brief summary of the note.
        /// </summary>
        public string Summary { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the detailed content of the note.
        /// </summary>
        public string Details { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the UTC timestamp when the note was created.
        /// </summary>
        public DateTimeOffset CreatedDateUtc { get; set; }

        /// <summary>
        /// Gets or sets the UTC timestamp when the note was last modified.
        /// Null if the note has never been modified.
        /// </summary>
        public DateTimeOffset? ModifiedDateUtc { get; set; }

        /// <summary>
        /// Gets or sets the list of tag names associated with this note.
        /// </summary>
        public List<string> Tags { get; set; } = new List<string>();
    }
}
