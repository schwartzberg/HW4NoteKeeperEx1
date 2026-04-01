using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HW4AzureFunctionsEx1
{
    /// <summary>
    /// Provides a <see cref="TableClient"/> for the Azure Storage "Jobs" table,
    /// authenticated with <see cref="DefaultAzureCredential"/> when a service URI is available,
    /// or via a connection string for local development.
    /// </summary>
    public class TableStorageHelper
    {
        private readonly TableClient _tableClient;
        private readonly ILogger<TableStorageHelper> _logger;

        /// <summary>
        /// The name of the Jobs table in Azure Table Storage.
        /// </summary>
        public const string JobsTableName = "Jobs";

        /// <summary>
        /// Initialises the helper by resolving the table connection from configuration.
        /// <para>
        /// Resolution order:
        /// 1. <c>StorageTableServiceUri</c> — explicit URI → <see cref="DefaultAzureCredential"/>
        /// 2. <c>AzureWebJobsStorage:tableServiceUri</c> — from env var split → <see cref="DefaultAzureCredential"/>
        /// 3. <c>AzureWebJobsStorage</c> — full connection string (local dev)
        /// </para>
        /// </summary>
        public TableStorageHelper(IConfiguration configuration, ILogger<TableStorageHelper> logger)
        {
            _logger = logger;

            // Try explicit URI first
            string? tableUri = configuration["StorageTableServiceUri"];

            // Fall back to AzureWebJobsStorage split key (env var AzureWebJobsStorage__tableServiceUri)
            if (string.IsNullOrEmpty(tableUri))
                tableUri = configuration["AzureWebJobsStorage:tableServiceUri"];

            if (!string.IsNullOrEmpty(tableUri))
            {
                var serviceClient = new TableServiceClient(new Uri(tableUri), new DefaultAzureCredential());
                _tableClient = serviceClient.GetTableClient(JobsTableName);
                _logger.LogInformation("TableStorageHelper initialised with URI {Uri}", tableUri);
            }
            else
            {
                // Fall back to connection string (local development)
                string? connectionString = configuration["AzureWebJobsStorage"];
                if (string.IsNullOrEmpty(connectionString))
                    throw new InvalidOperationException(
                        "No table storage configuration found. Set StorageTableServiceUri, " +
                        "AzureWebJobsStorage__tableServiceUri, or AzureWebJobsStorage.");

                var serviceClient = new TableServiceClient(connectionString);
                _tableClient = serviceClient.GetTableClient(JobsTableName);
                _logger.LogInformation("TableStorageHelper initialised with AzureWebJobsStorage connection string");
            }
        }

        /// <summary>Gets the <see cref="TableClient"/> for the Jobs table.</summary>
        public TableClient Client => _tableClient;
    }
}
