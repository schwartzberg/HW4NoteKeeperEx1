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
    /// HTTP-triggered test function that calls the same <see cref="AttachmentZipProcessor"/>
    /// used by the queue-triggered function. Useful for debugging in the Azure Portal
    /// (Code + Test → Test/Run) without needing a queue message.
    /// </summary>
    public class AttachmentZipHttpTestFunction
    {
        private readonly AttachmentZipProcessor _processor;
        private readonly ILogger<AttachmentZipHttpTestFunction> _logger;

        public AttachmentZipHttpTestFunction(AttachmentZipProcessor processor, ILogger<AttachmentZipHttpTestFunction> logger)
        {
            _processor = processor;
            _logger = logger;
        }

        [Function("AttachmentZipHttpTest")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
            FunctionContext context)
        {
            _logger.LogInformation("AttachmentZipHttpTest triggered.");

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
                await okResp.WriteStringAsync($"Zip created successfully for note {request.NoteId}");
                return okResp;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing zip request via HTTP test.");
                var errResp = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errResp.WriteStringAsync($"Error: {ex.Message}");
                return errResp;
            }
        }
    }
}
#endif
