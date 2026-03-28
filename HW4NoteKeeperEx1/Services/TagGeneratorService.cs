using Azure;
using HW4NoteKeeperEx1.Controllers;
using HW4NoteKeeperEx1.Settings;
using Microsoft.Extensions.AI;
using NJsonSchema;
using System.Text.Json;

namespace HW4NoteKeeperEx1.Services
{
    /// <summary>
    /// Service for generating keyword tags from note content using Azure OpenAI.
    /// </summary>
    public class TagGeneratorService
    {
        private readonly IChatClient _chatClient;
        private readonly AISettings _aISettings;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="TagGeneratorService"/> class.
        /// </summary>
        /// <param name="chatClient">The AI chat client for communicating with Azure OpenAI.</param>
        /// <param name="aISettings">Configuration settings for AI model behavior.</param>
        /// <param name="logger">Logger for tracking service operations and errors.</param>
        public TagGeneratorService(IChatClient chatClient, AISettings aISettings, ILogger logger)
        {
            _aISettings = aISettings;
            _chatClient = chatClient;
            _logger = logger;
        }

        /// <summary>
        /// Generates 3-5 keyword tags from the provided note details using Azure OpenAI.
        /// </summary>
        /// <param name="details">The note content to analyze for tag generation.</param>
        /// <returns>A <see cref="KeyTagsResponse"/> containing the generated tags.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the AI model returns null/empty response or deserialization fails.
        /// </exception>
        public async Task<KeyTagsResponse> GenerateTags(string details)
        {
            const int maxRetries = 3;
            int retryDelay = 1000; // Start with 1 second

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    JsonSchema schema = JsonSchema.FromType<KeyTagsResponse>();
                    string jsonSchemaString = schema.ToJson();
                    JsonElement jsonSchemaElement = JsonDocument.Parse(jsonSchemaString).RootElement;

                    ChatResponseFormatJson chatResponseFormatJson =
                        ChatResponseFormat.ForJsonSchema(jsonSchemaElement, "ChatResponse", "Chat response schema");

                    ChatOptions chatOptions = new ChatOptions()
                    {
                        Temperature = _aISettings.Temperature,
                        TopP = _aISettings.TopP,
                        MaxOutputTokens = _aISettings.MaxOutputTokens,
                        ResponseFormat = chatResponseFormatJson
                    };

                    var prompt = new[]
                    {
                        new ChatMessage(ChatRole.System,
                             "Return a JSON object with a 'Tags' array (one word in each 'Tag') containing keywords that summarize the essence of the user's text. " +
                             "The 'Tags' (each is one word) should be concise, relevant, and capture the main themes or topics of the text. " +
                             "Example: {\"Tags\": [\"keyword1\", \"keyword2\", \"keyword3\"]}"
                            ),
                        new ChatMessage(ChatRole.User, details)
                    };
                    
                    ChatResponse? chatResponse = await _chatClient.GetResponseAsync(prompt, chatOptions);

                    _logger.LogInformation("Attempt {Attempt}: Raw AI response: {Response}, Finish Reason: {FinishReason}", 
                        attempt + 1, chatResponse.Text ?? "(null)", chatResponse.FinishReason);

                    if (string.IsNullOrWhiteSpace(chatResponse.Text))
                    {
                        if (attempt < maxRetries - 1) // Don't retry on last attempt
                        {
                            _logger.LogWarning("AI returned empty response on attempt {Attempt}. Retrying after {Delay}ms...", 
                                attempt + 1, retryDelay);
                            await Task.Delay(retryDelay);
                            retryDelay *= 2; // Exponential backoff
                            continue;
                        }
                        
                        _logger.LogError("AI model returned null or empty response after {Attempts} attempts. Finish Reason: {FinishReason}", 
                            maxRetries, chatResponse.FinishReason);
                        throw new InvalidOperationException($"AI model returned null or empty response after {maxRetries} attempts. Finish Reason: {chatResponse.FinishReason}"); 
                    }

                    var response = JsonSerializer.Deserialize<KeyTagsResponse>(chatResponse.Text);

                    // Validate that the response was successfully deserialized and try again (to max 3 times) if not
                    if (response == null || response.Tags == null || response.Tags.Count == 0)
                    {
                        if (attempt < maxRetries - 1)
                        {
                            _logger.LogWarning("Failed to deserialize or empty tags on attempt {Attempt}. Retrying...", attempt + 1);
                            await Task.Delay(retryDelay);
                            retryDelay *= 2;
                            continue;
                        }
                        
                        _logger.LogError("Failed to deserialize response after {Attempts} attempts", maxRetries);
                        throw new InvalidOperationException("Failed to deserialize response from AI model");
                    }

                    _logger.LogInformation("Successfully generated {Count} tags on attempt {Attempt}", 
                        response.Tags.Count, attempt + 1); 
                     
                    return response;
                }
                catch (RequestFailedException ex) when (ex.Status == 429) // Rate limiting
                {
                    if (attempt < maxRetries - 1)
                    {
                        _logger.LogWarning("Rate limited on attempt {Attempt}. Retrying after {Delay}ms...", 
                            attempt + 1, retryDelay);
                        await Task.Delay(retryDelay);
                        retryDelay *= 2;
                        continue;
                    }
                    throw;
                }
            }

            throw new InvalidOperationException("Failed to generate tags after all retry attempts");
        } 
    }
}