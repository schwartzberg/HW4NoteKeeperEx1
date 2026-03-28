using HW4AzureFunctionsEx1.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace HW4AzureFunctionsEx1
{
    /// <summary>
    /// Azure Function triggered by messages in the <c>attachment-zip-requests</c> queue.
    /// Delegates all business logic to <see cref="AttachmentZipProcessor"/>.
    /// </summary>
    public class AttachmentZipFunction
    {
        private readonly AttachmentZipProcessor _processor;
        private readonly ILogger<AttachmentZipFunction> _logger;

        public AttachmentZipFunction(AttachmentZipProcessor processor, ILogger<AttachmentZipFunction> logger)
        {
            _processor = processor;
            _logger = logger;
        }

        [Function("AttachmentZipFunction")]
        public async Task Run(
            [QueueTrigger("attachment-zip-requests", Connection = "AttachmentZipRequests")] string message,
            FunctionContext context)
        {
            _logger.LogInformation("AttachmentZipFunction triggered. RawMessage={Message}", message);

            ZipRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<ZipRequest>(message,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }   
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialise queue message: {Message}", message);
                throw;
            }

            if (request is null)
            {
                _logger.LogError("Deserialized request is null for message: {Message}", message);
                throw new InvalidOperationException("Deserialized ZipRequest is null.");
            }

            await _processor.ProcessAsync(request);
        }
    }
}
