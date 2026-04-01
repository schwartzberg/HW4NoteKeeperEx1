using HW4AzureFunctionsEx1.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

#if DEBUG
namespace HW4AzureFunctionsEx1
{
    /// <summary>
    /// HTTP-triggered test function that calls the same <see cref="AttachmentZipProcessorLegacy"/>
    /// used by the legacy queue-triggered function. Useful for local testing via
    /// <c>func start</c> without needing a queue message.
    /// </summary>
    public class AttachmentZipHttpTestFunctionLegacy
    {
        private readonly AttachmentZipProcessorLegacy _processor;
        private readonly ILogger<AttachmentZipHttpTestFunctionLegacy> _logger;

        public AttachmentZipHttpTestFunctionLegacy(AttachmentZipProcessorLegacy processor, ILogger<AttachmentZipHttpTestFunctionLegacy> logger)
        {
            _processor = processor;
            _logger = logger;
        }

        [Function("AttachmentZipHttpTestLegacy")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
            FunctionContext context)
        {
            _logger.LogInformation("AttachmentZipHttpTestLegacy triggered.");

            string body = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("Request body: {Body}", body);

            ZipRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<ZipRequest>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to deserialise request body.");
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteStringAsync($"Invalid JSON: {ex.Message}");
                return badReq;
            }

            if (request is null || string.IsNullOrWhiteSpace(request.NoteId) || string.IsNullOrWhiteSpace(request.ZipFileId))
            {
                var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
                await badReq.WriteStringAsync("Body must contain {\"noteId\":\"...\",\"zipFileId\":\"...\"}");
                return badReq;
            }

            try
            {
                await _processor.ProcessAsync(request);
                var okResp = req.CreateResponse(HttpStatusCode.OK);
                await okResp.WriteStringAsync($"Legacy zip created successfully for note {request.NoteId}");
                return okResp;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing legacy zip request via HTTP test.");
                var errResp = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errResp.WriteStringAsync($"Error: {ex.Message}");
                return errResp;
            }
        }
    }
}
#endif
