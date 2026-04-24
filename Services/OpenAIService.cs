using System.Text.Json;
using OpenAI.Chat;

namespace DeerBalak.Services
{
    public interface IOpenAIService
    {
        Task<AiAnalysisResult> AnalyzePostAsync(string text);
    }

    public sealed class OpenAIService : IOpenAIService
    {
        private const int MaxRiskScore = 5;
        private const int MaxConfidence = 100;
        private const int MinConfidence = 20;
        private readonly bool _isEnabled;
        private readonly ChatClient? _chatClient;
        private readonly ILogger<OpenAIService> _logger;

        public OpenAIService(IConfiguration configuration, ILogger<OpenAIService> logger)
        {
            _logger = logger;

            // === OpenAI DEBUG ===
            Console.WriteLine("=== OpenAI DEBUG ===");
            var enabled = configuration.GetValue<bool>("OpenAI:Enabled", true);
            Console.WriteLine("Enabled from config: " + enabled);

            var apiKeyFromEnv = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var apiKeyFromConfig = configuration["OpenAI:ApiKey"];
            Console.WriteLine("API Key from env exists: " + (!string.IsNullOrEmpty(apiKeyFromEnv)));
            Console.WriteLine("API Key from config exists: " + (!string.IsNullOrEmpty(apiKeyFromConfig)));
            Console.WriteLine("API Key from env length: " + (apiKeyFromEnv?.Length ?? 0));
            Console.WriteLine("API Key from config length: " + (apiKeyFromConfig?.Length ?? 0));
            // === END DEBUG ===

            _isEnabled = enabled;

            // Get API key from environment variable first, then config
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ??
                        configuration["OpenAI:ApiKey"];

            if (_isEnabled && !string.IsNullOrWhiteSpace(apiKey))
            {
                try
                {
                    _chatClient = new ChatClient("gpt-4o-mini", apiKey);
                    _logger.LogInformation("✅ OpenAI service initialized successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Failed to initialize OpenAI client");
                }
            }
            else if (_isEnabled)
            {
                _logger.LogWarning("⚠️ OpenAI is enabled but no API key found");
            }
        }

        public async Task<AiAnalysisResult> AnalyzePostAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogDebug("Empty text provided, using fallback analysis");
                return AnalyzeWithRules(string.Empty);
            }

            if (!_isEnabled)
            {
                _logger.LogWarning("⚠️ OpenAI integration is disabled by configuration. Using local fallback.");
                return AnalyzeWithRules(text);
            }

            if (_chatClient == null)
            {
                _logger.LogWarning("⚠️ OpenAI client not available. Using fallback analysis.");
                return AnalyzeWithRules(text);
            }

            try
            {
                Console.WriteLine("CALLING OPENAI NOW...");
                _logger.LogInformation("🚀 Calling OpenAI API for analysis");
                var result = await AnalyzeWithOpenAIAsync(text);
                _logger.LogInformation("✅ OpenAI analysis completed: RiskScore={RiskScore}, Confidence={Confidence}%",
                    result.RiskScore, result.Confidence);
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
                Console.WriteLine("StackTrace: " + ex.StackTrace);
                _logger.LogError(ex, "❌ OpenAI API call failed: {Message}. Using fallback analysis.", ex.Message);
                // Temporarily throw to see the error
                throw;
                // return AnalyzeWithRules(text);
            }
        }

        private async Task<AiAnalysisResult> AnalyzeWithOpenAIAsync(string text)
        {
            const string prompt = @"You are a misinformation detection expert. Analyze the following social media post for potential misinformation, fake news, or harmful content.

Return a JSON response with this exact structure:
{
  ""riskScore"": number (0-5, where 0=no risk, 5=extremely high risk),
  ""confidence"": number (0-100, your confidence in this assessment),
  ""category"": ""string (Fake News|Misinformation|Safety|Political|Medical|Spam|Normal)"",
  ""flags"": [""array of strings like: 'sensational', 'unverified', 'emotional', 'urgent', 'political'"",
  ""explanation"": ""brief explanation of your analysis""
}

Consider:
- Sensational language, urgency, emotional appeals
- Unverified claims, conspiracy theories
- Harmful misinformation about health, safety, politics
- Spam or manipulative content

Post to analyze: ";

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage("You are a helpful assistant that analyzes social media posts for misinformation."),
                new UserChatMessage(prompt + text)
            };

            var options = new ChatCompletionOptions
            {
                Temperature = 0.1f, // Low temperature for consistent analysis
                MaxOutputTokenCount = 500
            };

            _logger.LogDebug("📤 Sending request to OpenAI API");
            var response = await _chatClient!.CompleteChatAsync(messages, options);

            if (response.Value.Content.Count == 0)
            {
                throw new Exception("Empty response from OpenAI");
            }

            var content = response.Value.Content[0].Text;
            _logger.LogDebug("📥 OpenAI raw response: {Response}", content);

            // Parse JSON response
            try
            {
                var result = JsonSerializer.Deserialize<AiAnalysisResult>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result == null)
                {
                    throw new Exception("Failed to deserialize OpenAI response");
                }

                // Validate and clamp values
                result.RiskScore = Math.Clamp(result.RiskScore, 0, MaxRiskScore);
                result.Confidence = Math.Clamp(result.Confidence, 0, MaxConfidence);
                result.Flags ??= new List<string>();

                return result;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse OpenAI JSON response: {Content}", content);
                throw;
            }
        }

        private AiAnalysisResult AnalyzeWithRules(string text)
        {
            var riskScore = 0;
            var flags = new List<string>();
            var lowerText = text?.ToLowerInvariant() ?? string.Empty;

            // High-risk keywords (+2 points)
            if (ContainsAny(lowerText, "danger", "disaster", "attack", "crisis", "emergency", "evacuate", "bomb", "explosion"))
            {
                riskScore += 2;
                flags.Add("danger");
            }

            // Medium-risk keywords (+1 point)
            if (ContainsAny(lowerText, "urgent", "breaking", "immediate", "pandemic", "virus"))
            {
                riskScore += 1;
                flags.Add("urgent");
            }

            // Uncertainty indicators (+1 point)
            if (ContainsAny(lowerText, "heard", "maybe", "not sure", "allegedly", "reportedly", "people say"))
            {
                riskScore += 1;
                flags.Add("uncertain");
            }

            // Pressure tactics (+1 point)
            if (ContainsAny(lowerText, "share now", "everyone must", "act fast", "don't wait", "breaking news"))
            {
                riskScore += 1;
                flags.Add("pressure");
            }

            // Sensational language (+1 point)
            if (ContainsAny(lowerText, "shocking", "unbelievable", "incredible", "amazing", "worst ever"))
            {
                riskScore += 1;
                flags.Add("sensational");
            }

            // All caps detection (+1 point for excessive caps)
            var upperCaseRatio = (double)(text?.Count(char.IsUpper) ?? 0) / (text?.Length ?? 1);
            if (upperCaseRatio > 0.7 && (text?.Length ?? 0) > 10)
            {
                riskScore += 1;
                flags.Add("all_caps");
            }

            riskScore = Math.Clamp(riskScore, 0, MaxRiskScore);

            var category = flags.Contains("danger") ? "Safety" :
                          flags.Contains("urgent") ? "Alert" :
                          flags.Contains("uncertain") ? "Uncertain" : "General";

            var confidence = MinConfidence + riskScore * 12; // Scale confidence with risk score
            confidence = Math.Clamp(confidence, MinConfidence, MaxConfidence);

            return new AiAnalysisResult
            {
                RiskScore = riskScore,
                Confidence = confidence,
                Category = category,
                Flags = flags,
                Explanation = BuildFallbackExplanation(riskScore, flags)
            };
        }

        private static bool ContainsAny(string source, params string[] terms)
        {
            return terms.Any(term => source.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildFallbackExplanation(int riskScore, IReadOnlyCollection<string> flags)
        {
            if (riskScore == 0)
            {
                return "No strong risk signals found. Local rule-based analysis used.";
            }

            return "OpenAI is unavailable. Local rule-based analysis used to estimate risk based on keywords." +
                   (flags.Any() ? $" Flags: {string.Join(", ", flags)}." : string.Empty);
        }
    }

    public sealed class AiAnalysisResult
    {
        public int RiskScore { get; set; }
        public int Confidence { get; set; }
        public string Category { get; set; } = string.Empty;
        public List<string> Flags { get; set; } = new();
        public string Explanation { get; set; } = string.Empty;
    }
}
