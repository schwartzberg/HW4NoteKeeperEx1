namespace HW4NoteKeeperEx1.RequestAndResultObjects
{
    /// <summary>
    /// API response model representing the status of a zip-creation job
    /// as stored in the Azure Storage "Jobs" table.
    /// Returned by the <c>GET notes/{noteId}/attachmentzipfiles/jobs</c> endpoints (§2.5, §3).
    /// </summary>
    public class JobStatusResponse
    {
        /// <summary>
        /// The id of the zip file in blob storage (e.g. "4b7134af-6cc3-4aad-bb54-0da2e0f07928.zip").
        /// </summary>
        public string ZipFileId { get; set; } = string.Empty;

        /// <summary>
        /// The insertion/last-modified time in UTC.
        /// </summary>
        public DateTimeOffset TimeStamp { get; set; }

        /// <summary>
        /// The status of the job: <c>Queued</c>, <c>InProgress</c>, <c>Completed</c>, or <c>Failed</c>.
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// A more detailed representation of the status.
        /// </summary>
        public string StatusDetails { get; set; } = string.Empty;
    }
}
