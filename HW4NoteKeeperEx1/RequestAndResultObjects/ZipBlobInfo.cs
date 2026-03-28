namespace HW4NoteKeeperEx1.RequestAndResultObjects
{
    /// <summary>
    /// Represents summary information about a zip blob in the zip container.
    /// </summary>
    public class ZipBlobInfo
    {
        /// <summary>
        /// The zip file name (blob name), e.g. "guid.zip".
        /// </summary>
        public string ZipFileId { get; set; } = string.Empty;

        /// <summary>
        /// The content type of the blob (always "application/zip").
        /// </summary>
        public string ContentType { get; set; } = "application/zip";

        /// <summary>
        /// The UTC date/time when the zip blob was created.
        /// </summary>
        public DateTimeOffset CreatedDate { get; set; }

        /// <summary>
        /// The UTC date/time when the zip blob was last modified.
        /// </summary>
        public DateTimeOffset LastModifiedDate { get; set; }

        /// <summary>
        /// The size of the zip blob in bytes.
        /// </summary>
        public long Length { get; set; }
    }
}
