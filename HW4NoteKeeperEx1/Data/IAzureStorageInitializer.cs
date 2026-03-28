namespace HW4NoteKeeperEx1.Data
{
    /// <summary>
    /// Interface for Azure Blob Storage initialization operations.
    /// Handles seeding blob containers and attachments for notes.
    /// </summary>
    public interface IAzureStorageInitializer
    {
        /// <summary>
        /// Clears all messages from the zip-requests queue and the poison queue.
        /// Called during seeding so stale messages don't trigger the function after a fresh deploy.
        /// </summary>
        Task ClearQueuesAsync();

        /// <summary>
        /// Deletes all blob containers in the storage account.
        /// Called before seeding to ensure a clean slate.
        /// </summary>
        Task DeleteAllContainersAsync();

        /// <summary>
        /// Seeds a single blob container with attachments for a specific note.
        /// Creates a private container named with the note's ID and uploads the associated attachment files.
        /// </summary>
        /// <param name="noteId">The GUID of the note (used as container name).</param>
        /// <param name="summary">The summary text of the note (used to look up attachments in mapping).</param>
        /// <param name="attachmentsDirectory">Path to directory containing attachment files. Defaults to AzureStorageAttachments folder.</param>
        /// <returns>True if container and attachments were seeded successfully; false otherwise.</returns>
        Task<bool> InitializeAsync(Guid noteId, string summary, string? attachmentsDirectory = null);

        /// <summary>
        /// Gets the attachment mapping dictionary that maps note summaries to their attachment file names.
        /// </summary>
        IReadOnlyDictionary<string, string[]> AttachmentMapping { get; }
    }
}
