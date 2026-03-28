namespace HW4NoteKeeperEx1.Models
{
    /// <summary>
    /// Represents the message payload enqueued to the attachment-zip-requests queue.
    /// </summary>
    public class ZipRequest
    {
        /// <summary>
        /// The ID of the note whose attachments should be zipped.
        /// </summary>
        public string NoteId { get; set; } = string.Empty;

        /// <summary>
        /// The target zip file name (e.g., "guid.zip") in the zip container.
        /// </summary>
        public string ZipFileId { get; set; } = string.Empty;
    }
}
