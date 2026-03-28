namespace HW4NoteKeeperEx1.Settings
{
    public class StorageAccountSettings : IStorageAccountSettings
    {
        /// <summary>
        /// Defines url for the container end point
        /// </summary>
        public required string ContainerEndpoint { get; set; }

        /// <summary>
        /// The tenant id where the storage account resides
        /// </summary>
        public required string TenantId { get; set; }

        /// <summary>
        /// Account name for the storage account
        /// </summary>
        public required string AccountName { get; set; }

        /// <summary>
        /// Account key for the storage account
        /// </summary>
        public required string AccountKey { get; set; }

        /// <summary>
        /// Url to the storage account
        /// </summary>
        public required string Url { get; set; } 
    }
}
