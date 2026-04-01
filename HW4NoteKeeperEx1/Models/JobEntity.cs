using Azure;
using Azure.Data.Tables;

namespace HW4NoteKeeperEx1.Models
{
    /// <summary>
    /// Represents a row in the Azure Storage "Jobs" table that tracks the status
    /// of an attachment zip-creation job.
    /// <para>
    /// <b>PartitionKey</b> = noteId (§1.2),
    /// <b>RowKey</b> = zipFileId (§1.1).
    /// </para>
    /// </summary>
    public class JobEntity : ITableEntity
    {
        /// <summary>The note ID (partition key per §1.2).</summary>
        public string PartitionKey { get; set; } = string.Empty;

        /// <summary>The zip file ID (row key per §1.1).</summary>
        public string RowKey { get; set; } = string.Empty;

        /// <summary>
        /// The status of the job: <c>Queued</c>, <c>InProgress</c>, <c>Completed</c>, or <c>Failed</c>.
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Detailed status information. Format varies by status per §2.4.1–2.4.4.
        /// </summary>
        public string StatusDetails { get; set; } = string.Empty;

        /// <summary>Auto-managed by Azure Table Storage.</summary>
        public DateTimeOffset? Timestamp { get; set; }

        /// <summary>Concurrency token managed by Azure Table Storage.</summary>
        public ETag ETag { get; set; }
    }
}
