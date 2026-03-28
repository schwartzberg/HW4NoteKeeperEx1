namespace HW4NoteKeeperEx1.Settings
{
    /// <summary>
    /// Configuration settings for Azure OpenAI integration.
    /// These settings control the behavior of the AI model used for tag generation.
    /// </summary>
    public class AISettings
    {
        /// <summary>
        /// Gets or sets the Azure OpenAI deployment endpoint URI.
        /// </summary>
        /// <example>https://ai-csscie94-foundry.openai.azure.com/</example>
        public required string DeploymentUri { get; set; } = "https://ai-csscie94-foundry.openai.azure.com/";

        /// <summary>
        /// Gets or sets the API key for authenticating with Azure OpenAI.
        /// </summary>
        public required string ApiKey { get; set; }

        /// <summary>
        /// Gets or sets the name of the deployed AI model.
        /// </summary>
        /// <example>gpt-5-mini</example>
        public required string DeploymentModelName { get; set; } = "gpt-5-mini";

        /// <summary>
        /// Gets or sets the sampling temperature for AI responses (0.0-2.0).
        /// Higher values (e.g., 1.0) make output more random, lower values (e.g., 0.2) make it more deterministic.
        /// </summary>
        public required float Temperature { get; set; } = 1.0f;

        /// <summary>
        /// Gets or sets the nucleus sampling parameter (0.0-1.0).
        /// Controls diversity by considering only the top P probability mass of tokens.
        /// </summary>
        public required float TopP { get; set; } = 1.0f;

        /// <summary>
        /// Gets or sets the maximum number of tokens the AI model can generate in a response.
        /// </summary>
        public required int MaxOutputTokens { get; set; } = 1000;
    }
}
