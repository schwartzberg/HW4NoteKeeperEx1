using Azure;
using Azure.AI.OpenAI;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using HW4NoteKeeperEx1.CustomSettings;
using HW4NoteKeeperEx1.Data;
using HW4NoteKeeperEx1.Settings;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.PerfCounterCollector.QuickPulse;
using Microsoft.ApplicationInsights.SnapshotCollector;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.OpenApi;
using System.Reflection;

namespace HW4NoteKeeperEx1
{
    public partial class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
             
            // Access settings
            // Using custom settings so additional settings can be provided
            ApplicationInsights applicationInsightsSettings = builder.Configuration.GetSection(key: nameof(ApplicationInsights)).Get<ApplicationInsights>() ?? new ApplicationInsights();
            ApplicationInsightsServiceOptions aiOptions = new ApplicationInsightsServiceOptions();

            // Configures adaptive sampling
            // Disabling adaptive sampling allows all events to be recorded
            aiOptions.EnableAdaptiveSampling = applicationInsightsSettings.EnableAdaptiveSampling;

            // Configure the connection string which provides the key necessary to connect to the application insights instance
            aiOptions.ConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];

            // The following line enables Application Insights telemetry collection.
            builder.Services.AddApplicationInsightsTelemetry(aiOptions);

            // Register custom TelemetryInitializer to provide role name when running locally
            builder.Services.AddSingleton<ITelemetryInitializer, DevelopmentRoleNameTelemetryInitializer>();

            // Setup live monitoring key so authentication is enabled allowing filtering of events
            builder.Services.ConfigureTelemetryModule<QuickPulseTelemetryModule>((module, _) =>
            {
                module.AuthenticationApiKey = applicationInsightsSettings.AuthenticationApiKey;
            });

            // DEMO: Setup snapshot debugging
            if (applicationInsightsSettings.EnableSnapshotCollectorInSdk)
            {
                builder.Services.AddSnapshotCollector((configuration)
                    => builder.Configuration
                    .Bind(nameof(SnapshotCollectorConfiguration), configuration));
            }

            var loggerFactory = LoggerFactory.Create(builder =>
               {
                   builder.AddConsole();
               }
             );

            // Add services to the container. 
            builder.Services.AddControllers();
            
            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            //builder.Services.AddOpenApi();

            // Move AddSwaggerGen here, before builder.Build()
            builder.Services.AddSwaggerGen(c =>
            {
                // Add nice title
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Note Keeper", Version = "v1" });

                // Add documentation via C# XML Comments
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

                // Only include XML comments if the file exists
                if (File.Exists(xmlPath))
                {
                    c.IncludeXmlComments(xmlPath);
                }
            }); 

            // Bind AISettings from appsettings.json or user secrets
            AISettings? _aiSettings = builder.Configuration.GetSection("AzureOpenAI").Get<AISettings>()!;

           
            // Validate AISettings to ensure they are not null or empty
            ILogger logger = loggerFactory.CreateLogger("Program");

            if (_aiSettings is null
                || string.IsNullOrWhiteSpace(_aiSettings.DeploymentUri)
                || string.IsNullOrWhiteSpace(_aiSettings.ApiKey))
            {
                if (_aiSettings == null)
                {
                    logger.LogCritical("AISettings is null. Please ensure the configuration is present.");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(_aiSettings.DeploymentUri))
                    {
                        logger.LogCritical("AISettings.DeploymentUri is null or empty.");
                    }
                    if (string.IsNullOrWhiteSpace(_aiSettings.ApiKey))
                    {
                        logger.LogCritical("AISettings.ApiKey is null or empty.");
                    }
                }

                throw new InvalidOperationException("AISettings validation failed. Check the logs for details.");
            }

            // Register AISettings
            logger.LogInformation("AISettings loaded successfully.");
            builder.Services.AddSingleton(_aiSettings!);

            // Bind NoteLimits from appsettings.json
            var noteLimits = builder.Configuration.GetSection("NoteLimits").Get<HW4NoteKeeperEx1.CustomSettings.NoteLimits>() 
                ?? new HW4NoteKeeperEx1.CustomSettings.NoteLimits();
            builder.Services.AddSingleton(implementationInstance: noteLimits);

            // Bind StorageOperationalSettings (queue names, protected containers) from appsettings.json
            var storageOperationalSettings = builder.Configuration
                .GetSection("StorageOperationalSettings")
                .Get<HW4NoteKeeperEx1.Settings.StorageOperationalSettings>()
                ?? new HW4NoteKeeperEx1.Settings.StorageOperationalSettings();
            builder.Services.AddSingleton(storageOperationalSettings);
            logger.LogInformation(
                "StorageOperationalSettings loaded: ZipQueue={Queue}, ProtectedContainers=[{Containers}]",
                storageOperationalSettings.ZipRequestsQueueName,
                string.Join(", ", storageOperationalSettings.ProtectedContainers));
             
            string connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

            builder.Services.AddDbContext<MyDatabaseContext>(options =>
            {
                options.UseSqlServer(connectionString);

                //if (builder.Environment.IsDevelopment())
                //    options.EnableSensitiveDataLogging();
            });

            logger.LogInformation("NoteLimits loaded: MaxNotes = {MaxNotes}, MaxAttachments = {MaxAttachments}",
                noteLimits.MaxNotes, noteLimits.MaxAttachments);

            // Bind StorageAccountSettings and register BlobServiceClient using managed identity
            var storageAccountSettings = builder.Configuration.GetSection("StorageAccountSettings").Get<HW4NoteKeeperEx1.Settings.StorageAccountSettings>();
            if (storageAccountSettings is null || string.IsNullOrWhiteSpace(storageAccountSettings.Url))
            {
                logger.LogCritical("StorageAccountSettings is missing or Url is not configured.");
                throw new InvalidOperationException("StorageAccountSettings validation failed. Check the logs for details.");
            }

            builder.Services.AddSingleton(storageAccountSettings);
            RegisterBlobServiceClient(builder, storageAccountSettings);
            RegisterQueueServiceClient(builder, storageAccountSettings);
            builder.Services.AddScoped<HW4NoteKeeperEx1.Services.AzureStorageService>();
            RegisterTableClient(builder, storageAccountSettings);
            builder.Services.AddSingleton<HW4NoteKeeperEx1.Services.JobsTableService>();

            // Register AzureStorageInitializer as singleton for seeding operations
            builder.Services.AddSingleton<HW4NoteKeeperEx1.Data.IAzureStorageInitializer, HW4NoteKeeperEx1.Data.AzureStorageInitializer>();

            // Initialize OpenAI service endpoint and API key credential
            Uri openAIServiceEndpointUri = new Uri(_aiSettings.DeploymentUri);
            AzureKeyCredential apiKeyCredential = new AzureKeyCredential(_aiSettings.ApiKey);
            RegisterOpenAIClient(builder, 
                                 openAIServiceEndpointUri, 
                                 apiKeyCredential, 
                                 _aiSettings.DeploymentModelName);

            var app = builder.Build();

            // Seed the database and Azure Blob Storage (always wipes and reseeds on startup)
            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                try
                {
                    var context = services.GetRequiredService<MyDatabaseContext>();
                    var chatClient = services.GetRequiredService<IChatClient>();
                    var aiSettings = services.GetRequiredService<AISettings>();
                    var scopedLogger = services.GetRequiredService<ILogger<Program>>();
                    var telemetryClient = services.GetRequiredService<TelemetryClient>();
                    var storageInitializer = services.GetRequiredService<HW4NoteKeeperEx1.Data.IAzureStorageInitializer>();

                    logger.LogInformation("Starting database and storage initialization...");

                    // Create DbInitializer instance and call InitializeAsync
                    // (InitializeAsync will handle EnsureCreatedAsync internally)
                    var dbInitializer = new HW4NoteKeeperEx1.Data.DbInitializer(
                        context, chatClient, aiSettings, 
                        services.GetRequiredService<ILogger<HW4NoteKeeperEx1.Data.DbInitializer>>(), 
                        telemetryClient, storageInitializer);
                    
                    await dbInitializer.InitializeAsync();
                    
                    logger.LogInformation("Database and storage initialization completed successfully.");
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "An error occurred during initialization. Application startup will be aborted.");
                    throw; // Rethrow to prevent the app from starting with an uninitialized database
                }
            }

            // Code Note: Moved outside of env.IsDevelopment() so both 
            // Debug and Release are supported

            app.UseSwagger();

            // Customize the UseSwaggerUI() 
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Note Keeper API V1");

                // Code Note: 
                // Launch the Swagger UI by default
                // Serving the Swagger UI at the app's root 
                // (http://localhost:<port>)
                c.RoutePrefix = string.Empty; 
            }); 

            //app.MapOpenApi();

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }

        /// <summary>
        /// Registers a <see cref="BlobServiceClient"/> singleton using managed identity credentials
        /// scoped to the current hosting environment (developer credentials locally, managed identity in Azure).
        /// </summary>
        private static void RegisterBlobServiceClient(
            WebApplicationBuilder builder,
            HW4NoteKeeperEx1.Settings.StorageAccountSettings storageSettings)
        {
            var credentialOptions = new DefaultAzureCredentialOptions();

            if (builder.Environment.IsDevelopment())
            {
                // Local development: Only use developer-oriented credentials.
                credentialOptions.SharedTokenCacheTenantId = storageSettings.TenantId;
                credentialOptions.VisualStudioCodeTenantId = storageSettings.TenantId;
                credentialOptions.VisualStudioTenantId = storageSettings.TenantId;
                credentialOptions.ExcludeEnvironmentCredential = true;
                credentialOptions.ExcludeManagedIdentityCredential = true;
                credentialOptions.ExcludeWorkloadIdentityCredential = true;
                credentialOptions.ExcludeInteractiveBrowserCredential = true;
            }
            else
            {
                // Azure hosted: Only use cloud-hosted credentials.
                credentialOptions.ExcludeVisualStudioCredential = true;
                credentialOptions.ExcludeVisualStudioCodeCredential = true;
                credentialOptions.ExcludeAzureCliCredential = true;
                credentialOptions.ExcludeAzurePowerShellCredential = true;
                credentialOptions.ExcludeAzureDeveloperCliCredential = true;
                credentialOptions.ExcludeWorkloadIdentityCredential = true;
                credentialOptions.ExcludeInteractiveBrowserCredential = true;
            }

            var credential = new DefaultAzureCredential(credentialOptions);
            builder.Services.AddSingleton(new BlobServiceClient(new Uri(storageSettings.Url), credential));
        }

        /// <summary>
        /// Registers a <see cref="QueueServiceClient"/> singleton using managed identity credentials,
        /// constructing the queue service URI from the storage account name.
        /// </summary>
        private static void RegisterQueueServiceClient(
            WebApplicationBuilder builder,
            HW4NoteKeeperEx1.Settings.StorageAccountSettings storageSettings)
        {
            var credentialOptions = new DefaultAzureCredentialOptions();

            if (builder.Environment.IsDevelopment())
            {
                credentialOptions.SharedTokenCacheTenantId = storageSettings.TenantId;
                credentialOptions.VisualStudioCodeTenantId = storageSettings.TenantId;
                credentialOptions.VisualStudioTenantId = storageSettings.TenantId;
                credentialOptions.ExcludeEnvironmentCredential = true;
                credentialOptions.ExcludeManagedIdentityCredential = true;
                credentialOptions.ExcludeWorkloadIdentityCredential = true;
                credentialOptions.ExcludeInteractiveBrowserCredential = true;
            }
            else
            {
                credentialOptions.ExcludeVisualStudioCredential = true;
                credentialOptions.ExcludeVisualStudioCodeCredential = true;
                credentialOptions.ExcludeAzureCliCredential = true;
                credentialOptions.ExcludeAzurePowerShellCredential = true;
                credentialOptions.ExcludeAzureDeveloperCliCredential = true;
                credentialOptions.ExcludeWorkloadIdentityCredential = true;
                credentialOptions.ExcludeInteractiveBrowserCredential = true;
            }

            var credential = new DefaultAzureCredential(credentialOptions);
            var queueServiceUri = new Uri($"https://{storageSettings.AccountName}.queue.core.windows.net");
            builder.Services.AddSingleton(new QueueServiceClient(queueServiceUri, credential));
        }

        /// <summary>
        /// Registers a <see cref="TableClient"/> singleton for the Jobs table using managed identity credentials,
        /// constructing the table service URI from the storage account name.
        /// </summary>
        private static void RegisterTableClient(
            WebApplicationBuilder builder,
            HW4NoteKeeperEx1.Settings.StorageAccountSettings storageSettings)
        {
            var credentialOptions = new DefaultAzureCredentialOptions();

            if (builder.Environment.IsDevelopment())
            {
                credentialOptions.SharedTokenCacheTenantId = storageSettings.TenantId;
                credentialOptions.VisualStudioCodeTenantId = storageSettings.TenantId;
                credentialOptions.VisualStudioTenantId = storageSettings.TenantId;
                credentialOptions.ExcludeEnvironmentCredential = true;
                credentialOptions.ExcludeManagedIdentityCredential = true;
                credentialOptions.ExcludeWorkloadIdentityCredential = true;
                credentialOptions.ExcludeInteractiveBrowserCredential = true;
            }
            else
            {
                credentialOptions.ExcludeVisualStudioCredential = true;
                credentialOptions.ExcludeVisualStudioCodeCredential = true;
                credentialOptions.ExcludeAzureCliCredential = true;
                credentialOptions.ExcludeAzurePowerShellCredential = true;
                credentialOptions.ExcludeAzureDeveloperCliCredential = true;
                credentialOptions.ExcludeWorkloadIdentityCredential = true;
                credentialOptions.ExcludeInteractiveBrowserCredential = true;
            }

            var credential = new DefaultAzureCredential(credentialOptions);
            var tableServiceUri = new Uri($"https://{storageSettings.AccountName}.table.core.windows.net");
            var tableServiceClient = new TableServiceClient(tableServiceUri, credential);

            // Get or create the Jobs table
            string jobsTableName = builder.Configuration["StorageOperationalSettings:JobsTableName"] ?? "Jobs";
            var tableClient = tableServiceClient.GetTableClient(jobsTableName);
            builder.Services.AddSingleton(tableClient);
        }

        /// <summary>
        /// Registers the OpenAI client with the specified parameters.
        /// This code is from the lectures
        /// </summary>
        /// <param name="builder">The web application builder.</param>
        /// <param name="openAIServiceEndpointUri">The OpenAI service endpoint URI.</param>
        /// <param name="apiKeyCredential">The API key credential.</param>
        /// <param name="deploymentName">The deployment name.</param>
        private static void RegisterOpenAIClient(WebApplicationBuilder builder,
                                             Uri openAIServiceEndpointUri,
                                             AzureKeyCredential apiKeyCredential,
                                             string deploymentName)
        {
            // Register the OpenAI client as a singleton service
            // A singleton service is created once and shared throughout the application's lifetime
            builder.Services.AddSingleton<IChatClient>(services =>
            {
                var azureOpenAiClient = new AzureOpenAIClient(openAIServiceEndpointUri, apiKeyCredential);
                var chatClient = azureOpenAiClient.GetChatClient(deploymentName);
                return chatClient.AsIChatClient();
            });
        }

        static void ConfigureManagedIdentityForBlobStorage(
            WebApplicationBuilder builder,
            StorageAccountSettings storageAccountSettings,
            string containerName,
            string containerEndPoint)
        {
            // Sets up BlobContainerClient for the ManagedIdentity demo path, including
            // environment-scoped DefaultAzureCredential options and container bootstrap.
            // Scope the credential lookup order based on the hosting environment to avoid
            // wasting time probing credential types that will never succeed in that context.
            var credentialOptions = new DefaultAzureCredentialOptions();

            if (builder.Environment.IsDevelopment())
            {
                // Local development: Only use developer-oriented credentials.
                // ManagedIdentityCredential and EnvironmentCredential are excluded because they
                // are not available on a developer workstation and would cause unnecessary delays
                // or confusing timeout errors before falling through to the credentials that work.
                credentialOptions.SharedTokenCacheTenantId = storageAccountSettings.TenantId;
                credentialOptions.VisualStudioCodeTenantId = storageAccountSettings.TenantId;
                credentialOptions.VisualStudioTenantId = storageAccountSettings.TenantId;
                credentialOptions.ExcludeEnvironmentCredential = true;
                credentialOptions.ExcludeManagedIdentityCredential = true;
                credentialOptions.ExcludeWorkloadIdentityCredential = true;
                credentialOptions.ExcludeInteractiveBrowserCredential = true;
            }
            else
            {
                // Azure hosted: Only use cloud-hosted credentials.
                // Developer credentials (Visual Studio, CLI, etc.) are excluded because they are
                // not available in Azure and would cause unnecessary delays before falling through
                // to EnvironmentCredential or ManagedIdentityCredential.
                credentialOptions.ExcludeVisualStudioCredential = true;
                credentialOptions.ExcludeVisualStudioCodeCredential = true;
                credentialOptions.ExcludeAzureCliCredential = true;
                credentialOptions.ExcludeAzurePowerShellCredential = true;
                credentialOptions.ExcludeAzureDeveloperCliCredential = true;
                credentialOptions.ExcludeWorkloadIdentityCredential = true;
                credentialOptions.ExcludeInteractiveBrowserCredential = true;
            }

            var managedIdentityCredential = new DefaultAzureCredential(credentialOptions);

            // DEMO 1: Use MANAGED IDENTITIES
            // Create the container if its not present using MANAGED IDENTITIES
            var blobServiceClient = new BlobServiceClient(new Uri(storageAccountSettings.Url), managedIdentityCredential);
            var blobContainerClient = new BlobContainerClient(new Uri(containerEndPoint), managedIdentityCredential);
            if (!blobContainerClient.Exists())
            {
                var _ = blobServiceClient.CreateBlobContainer(containerName);
            }

            // DEMO 1: Use MANAGED IDENTITIES
            // Register the BlobContainerClient with dependency injection using MANAGED IDENTITIES
            builder.Services.AddSingleton(new BlobContainerClient(new Uri(containerEndPoint), managedIdentityCredential));
        }
    } 
}

