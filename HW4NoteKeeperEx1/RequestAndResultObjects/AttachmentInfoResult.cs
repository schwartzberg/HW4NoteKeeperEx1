namespace HW4NoteKeeperEx1.RequestAndResultObjects
{
    /// <summary>
    /// Represents summary information about a blob attachment.
    /// </summary>
    public class AttachmentInfoResult
    {
        /// <summary>
        /// The id of the blob (filename).
        /// </summary>
        public string AttachmentId { get; set; } = string.Empty;

        /// <summary>
        /// The content type of the blob.
        /// </summary>
        public string ContentType { get; set; } = string.Empty;

        /// <summary>
        /// The date/time created as recorded by the blob SDK.
        /// </summary>
        public DateTimeOffset CreatedDate { get; set; }

        /// <summary>
        /// The last modified date as recorded by the blob SDK.
        /// </summary>
        public DateTimeOffset LastModifiedDate { get; set; }

        /// <summary>
        /// The length of the blob as recorded by the blob SDK.
        /// </summary>
        public long Length { get; set; }
    }
}
