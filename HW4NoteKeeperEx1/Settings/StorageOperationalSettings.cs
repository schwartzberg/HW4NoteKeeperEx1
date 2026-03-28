namespace HW4NoteKeeperEx1.Settings
{
    /// <summary>
    /// Operational settings for Azure Storage — queue names and containers that must never be deleted.
    /// Bound from the <c>StorageOperationalSettings</c> section of <c>appsettings.json</c>.
    /// In Azure App Service these values can be overridden with environment variables using
    /// double-underscore notation, e.g. <c>StorageOperationalSettings__ZipRequestsQueueName</c>.
    /// </summary>
    public class StorageOperationalSettings
    {
        /// <summary>
        /// Name of the Azure Storage Queue that receives zip-creation requests.
        /// Default: <c>attachment-zip-requests</c>.
        /// </summary>
        public string ZipRequestsQueueName { get; set; } = "attachment-zip-requests";

        /// <summary>
        /// Name of the Azure Storage Queue that receives unprocessable zip-creation requests (poison messages).
        /// Default: <c>attachment-zip-requests-poison</c>.
        /// </summary>
        public string ZipPoisonQueueName { get; set; } = "attachment-zip-requests-poison";

        /// <summary>
        /// Names of Azure Blob Storage containers that must <b>never</b> be deleted,
        /// even during the storage-reset that happens at seeding time.
        /// Default: <c>[ "app-package-func-hw4" ]</c>.
        /// </summary>
        public List<string> ProtectedContainers { get; set; } = new()
        {
            "app-package-func-hw4"
        };
    }
}
