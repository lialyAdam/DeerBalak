using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OpenAI.Chat;

namespace DeerBalak.Services
{
    public interface IOpenAIService
    {
        Task<AiAnalysisResult> AnalyzePostAsync(string text);
    }

    public sealed class OpenAIService : IOpenAIService
    {
        private const int MaxRiskScore = 10;
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
                var res = AnalyzeWithRules(string.Empty);
                RiskAssessmentService.Normalize(res, lowConfidenceMode: false);
                return res;
            }

            if (!_isEnabled)
            {
                _logger.LogWarning("⚠️ OpenAI integration is disabled by configuration. Using local fallback.");
                var res = AnalyzeWithRules(text);
                RiskAssessmentService.Normalize(res, lowConfidenceMode: true);
                return res;
            }

            if (_chatClient == null)
            {
                _logger.LogWarning("⚠️ OpenAI client not available. Using fallback analysis.");
                var res = AnalyzeWithRules(text);
                RiskAssessmentService.Normalize(res, lowConfidenceMode: true);
                return res;
            }

            try
            {
                Console.WriteLine("CALLING OPENAI NOW...");
                _logger.LogInformation("🚀 Calling OpenAI API for analysis");
                var result = await AnalyzeWithOpenAIAsync(text);
                _logger.LogInformation("✅ OpenAI analysis completed: RiskScore={RiskScore}, Confidence={Confidence}%",
                    result.RiskScore, result.Confidence);

                // Normalize final output to enforce single-source-of-truth rules
                RiskAssessmentService.Normalize(result, lowConfidenceMode: false);
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex.Message);
                Console.WriteLine("StackTrace: " + ex.StackTrace);
                _logger.LogError(ex, "❌ OpenAI API call failed: {Message}. Using fallback analysis.", ex.Message);
                var fallback = AnalyzeWithRules(text);
                RiskAssessmentService.Normalize(fallback, lowConfidenceMode: true);
                return fallback;
            }
        }

        private async Task<AiAnalysisResult> AnalyzeWithOpenAIAsync(string text)
        {
            const string prompt = @"You are a content safety and misinformation detection system for a social feed application.

Follow these rules strictly and return ONLY a single JSON object (no extra text).

1) FINAL DECISION: return exactly ONE of: SAFE, LOW RISK, MEDIUM RISK, HIGH RISK, CRITICAL.
2) NO CONTRADICTIONS: risk_level, recommendation, and status must all align with the single final decision.
3) CONFIDENCE: Represents certainty (0-100) only and must NOT change the risk level.
4) REPETITION SIGNAL: 'appeared X times' is a weak signal — it may slightly adjust score but cannot alone cause CRITICAL.
5) AI UNAVAILABLE: if AI is unavailable, add flag 'low_confidence_mode' and reduce sensitivity by at most one level (do not increase risk).
6) DEDUPLICATION: treat similar posts conceptually; do not escalate risk solely due to repetition.
7) CATEGORY: choose ONE of: Traffic, Safety, Emergency, News, General (prefer General over Other).
8) RECOMMENDATION MAPPING (must match risk level exactly):
   SAFE => "Content appears safe"
   LOW RISK => "Minor caution"
   MEDIUM RISK => "Verify before sharing"
   HIGH RISK => "Do not share without verification"
   CRITICAL => "Urgent risk: avoid sharing immediately"

OUTPUT FORMAT (STRICT JSON):
{
  ""risk_level"": ""SAFE"" | ""LOW RISK"" | ""MEDIUM RISK"" | ""HIGH RISK"" | ""CRITICAL"",
  ""confidence"": number (0-100),
  ""category"": ""Traffic"" | ""Safety"" | ""Emergency"" | ""News"" | ""General"",
  ""recommendation"": string (use exact mapping above),
  ""reason"": string (short explanation, optional)
}

Priority when deciding: 1) content meaning 2) context/intent 3) source consistency 4) repetition (weak) 5) keyword patterns.

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

                // Map alias fields if the model used different keys (e.g., "recommendation" / "reason")
                if (!string.IsNullOrWhiteSpace(result.Recommendation))
                {
                    result.RecommendedAction = result.Recommendation;
                }
                if (!string.IsNullOrWhiteSpace(result.Reason))
                {
                    result.WhyFlagged = result.Reason;
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
            double riskScore = 0.0;
            var flags = new List<string>();
            var lowerText = text?.ToLowerInvariant() ?? string.Empty;

            // Detect repetition pattern: "appeared X times" -> small boost up to +0.5
            var repetitionMatch = Regex.Match(lowerText, @"appeared\s+(\d+)\s+times");
            if (repetitionMatch.Success && int.TryParse(repetitionMatch.Groups[1].Value, out var repCount))
            {
                var boost = Math.Min(0.5, repCount * 0.1); // 0.1 per appearance, capped at 0.5
                riskScore += boost;
                flags.Add("repetition");
            }

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

            // Clamp internal score and convert to integer output
            riskScore = Math.Clamp(riskScore, 0.0, MaxRiskScore);
            var finalScore = (int)Math.Round(riskScore);

            // Category heuristics
            var category = flags.Contains("danger") ? "Safety" :
                          flags.Contains("urgent") ? "News" :
                          flags.Contains("uncertain") ? "News" : "News";

            var confidence = MinConfidence + finalScore * 8; // Scale confidence with risk score
            confidence = Math.Clamp(confidence, MinConfidence, MaxConfidence);

            return new AiAnalysisResult
            {
                RiskScore = finalScore,
                Confidence = confidence,
                Category = category,
                MainSignals = flags.ToList(),
                Flags = flags,
                WhyFlagged = flags.Any() ? string.Join(", ", flags) : "No risk signals detected.",
                RiskLevel = GetRiskLevel(finalScore),
                RecommendedAction = GetRecommendedAction(finalScore),
                Explanation = BuildFallbackExplanation(finalScore, flags)
            };
        }

        private static bool ContainsAny(string source, params string[] terms)
        {
            return terms.Any(term => source.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetRiskLevel(int score)
        {
            if (score <= 2) return "SAFE";
            if (score <= 4) return "LOW RISK";
            if (score <= 6) return "MEDIUM RISK";
            if (score <= 8) return "HIGH RISK";
            return "CRITICAL";
        }

        private static string GetRecommendedAction(int score)
        {
            var level = GetRiskLevel(score);
            return level switch
            {
                var s when s.Equals("SAFE", StringComparison.OrdinalIgnoreCase) => "Content appears safe",
                var s when s.Equals("LOW RISK", StringComparison.OrdinalIgnoreCase) => "Minor caution",
                var s when s.Equals("MEDIUM RISK", StringComparison.OrdinalIgnoreCase) => "Verify before sharing",
                var s when s.Equals("HIGH RISK", StringComparison.OrdinalIgnoreCase) => "Do not share",
                var s when s.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase) => "Urgent: avoid sharing",
                _ => "Verify before sharing"
            };
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
        [JsonPropertyName("risk_score")]
        public int RiskScore { get; set; }

        [JsonPropertyName("risk_level")]
        public string RiskLevel { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        public int Confidence { get; set; }

        [JsonPropertyName("main_signals")]
        public List<string> MainSignals { get; set; } = new();

        [JsonPropertyName("why_flagged")]
        public string WhyFlagged { get; set; } = string.Empty;

        [JsonPropertyName("recommended_action")]
        public string RecommendedAction { get; set; } = string.Empty;

        // Alias keys the model might return for compatibility with the new prompt
        [JsonPropertyName("recommendation")]
        public string Recommendation { get; set; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;
        public List<string> Flags { get; set; } = new();
        public string Explanation { get; set; } = string.Empty;
    }
}
