using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HW4AzureFunctionsEx1
{
    /// <summary>
    /// Provides a <see cref="BlobServiceClient"/> scoped to the storage account,
    /// authenticated with <see cref="DefaultAzureCredential"/> (managed identity in Azure,
    /// developer credentials locally).
    /// </summary>
    public class BlobStorageHelper
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<BlobStorageHelper> _logger;

        /// <summary>
        /// Initialises the helper by reading <c>StorageBlobServiceUri</c> from configuration
        /// and creating a <see cref="BlobServiceClient"/> with <see cref="DefaultAzureCredential"/>.
        /// </summary>
        public BlobStorageHelper(IConfiguration configuration, ILogger<BlobStorageHelper> logger)
        {
            _logger = logger;
            string blobUri = configuration["StorageBlobServiceUri"]
                ?? throw new InvalidOperationException("StorageBlobServiceUri is not configured.");

            _blobServiceClient = new BlobServiceClient(new Uri(blobUri), new DefaultAzureCredential());
            _logger.LogInformation("BlobStorageHelper initialised with URI {Uri}", blobUri);
        }

        /// <summary>Gets the underlying <see cref="BlobServiceClient"/>.</summary>
        public BlobServiceClient Client => _blobServiceClient;
    }
}
