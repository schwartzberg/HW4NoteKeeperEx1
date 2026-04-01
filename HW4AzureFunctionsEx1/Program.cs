using HW4AzureFunctionsEx1;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddSingleton<BlobStorageHelper>();
        services.AddSingleton<TableStorageHelper>();
        services.AddScoped<AttachmentZipProcessor>();
        services.AddScoped<AttachmentZipProcessorLegacy>();
    })
    .ConfigureLogging(logging =>
    {
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .Build();

await host.RunAsync();
