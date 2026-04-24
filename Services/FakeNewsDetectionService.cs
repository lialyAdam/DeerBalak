using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace Deerbalak.Data.Services
{
    public class FakeNewsDetectionService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<FakeNewsDetectionService> _logger;
        private readonly IMemoryCache _cache;
        private readonly ClaimTrackingService? _claimTrackingService;
        private readonly IAIService? _aiService;
        
        // Rate limiting: max 10 requests per minute
        private static readonly SemaphoreSlim _requestThrottle = new SemaphoreSlim(10, 10);
        private static readonly TimeSpan _throttleResetInterval = TimeSpan.FromMinutes(1);

        private const string PromptTemplate = @"
You are an advanced AI system for detecting misleading, risky, or fake content in social media posts.

Your job is to analyze the given text AND consider how many times this same or very similar content has appeared before.

IMPORTANT RULES:
- Return ONLY valid JSON (no markdown, no explanation outside JSON)
- Be precise, professional, and realistic
- Do NOT exaggerate risk unless justified
- Always include meaningful explanation and suggestions

INPUT:
Text: {0}
RepetitionCount: {1}

OUTPUT FORMAT:
{{
  ""score"": number (0-10),
  ""label"": ""SAFE | LOW | WARNING | HIGH | CRITICAL"",
  ""confidence"": number (0-100),
  ""explanation"": ""clear professional explanation"",
  ""category"": ""Fake News | Alert | Safety | Political | Medical | Other"",
  ""suggestions"": [""suggestion 1"", ""suggestion 2""]
}}

SCORING GUIDE:
0-2 → Safe  
3-4 → Minor concern  
5-6 → Moderate risk  
7-8 → High risk  
9-10 → Critical danger  

ANALYSIS RULES:

1. Content Risk:
- Detect urgency words (urgent, immediately, now, must)
- Detect emotional manipulation (fear, panic, shock)
- Detect lack of sources or vague claims
- Detect unrealistic or sensational claims

2. Repetition Impact:
- If RepetitionCount >= 3 → increase risk slightly
- If RepetitionCount >= 5 → increase risk moderately
- If RepetitionCount >= 10 → treat as possible misinformation spread
- Repeated content without verification is suspicious

3. Context Awareness:
- Short neutral text (like ""Hello"") = SAFE
- Vague alerts without details = LOW or WARNING
- Extreme claims without evidence = HIGH or CRITICAL

4. Explanation:
- Explain WHY the content is risky or safe
- Mention repetition effect if relevant

5. Suggestions:
- Give practical actions:
  - Verify sources
  - Avoid sharing
  - Wait for confirmation
  - Report if needed

EXAMPLE THINKING (DO NOT OUTPUT THIS):
- ""Aliens landed!!!"" + high repetition → CRITICAL
- ""Road closed"" → SAFE
- ""I heard something might happen"" → WARNING

Now analyze the input carefully.

RETURN ONLY JSON.
";

        public FakeNewsDetectionService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<FakeNewsDetectionService> logger,
            IMemoryCache cache,
            ClaimTrackingService? claimTrackingService = null,
            IAIService? aiService = null)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _cache = cache;
            _claimTrackingService = claimTrackingService;
            _aiService = aiService;
            
            // Configure HttpClient timeouts
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<FakeNewsAnalysisResult> AnalyzePostAsync(string postContent, string? userId = null, int postId = 0)
        {
            // Check cache first
            var cacheKey = $"FakeNewsAnalysis_{postContent.GetHashCode()}";
            if (_cache.TryGetValue(cacheKey, out FakeNewsAnalysisResult? cachedResult))
            {
                _logger.LogInformation("✅ Returning cached analysis result");
                return cachedResult!;
            }

            // Get claim tracking data for context
            var claimTracking = new ClaimTracking
            {
                IsRepeatedClaim = false,
                SimilarClaimsCount = 0,
                SpreadLevel = "LOW",
                FirstSeen = null
            };

            if (_claimTrackingService != null)
            {
                try
                {
                    var trackingResult = await _claimTrackingService.AnalyzeClaimSpreadAsync(postContent, postId);
                    claimTracking.IsRepeatedClaim = trackingResult.IsRepeatedClaim;
                    claimTracking.SimilarClaimsCount = trackingResult.SimilarClaimsCount;
                    claimTracking.SpreadLevel = trackingResult.SpreadLevel;
                    claimTracking.FirstSeen = trackingResult.FirstSeenDate.ToString("MMM dd, yyyy");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch claim tracking data");
                }
            }

            // Use the enhanced AI service
            if (_aiService != null)
            {
                try
                {
                    var aiResult = await _aiService.AnalyzeTextAsync(postContent);

                    var result = new FakeNewsAnalysisResult
                    {
                        Score = aiResult.risk_score,
                        Label = aiResult.label,
                        Confidence = aiResult.confidence,
                        Category = aiResult.category,
                        Explanation = aiResult.explanation,
                        Suggestions = new[] { aiResult.recommended_action },
                        ClaimTracking = claimTracking,
                        RecommendedAction = aiResult.recommended_action,
                        Mode = aiResult.mode,
                        Flags = aiResult.flags?.ToArray()
                    };

                    // Cache the result
                    _cache.Set(cacheKey, result, TimeSpan.FromHours(24));
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AI service failed, using fallback");
                }
            }

            // Fallback to basic analysis
            return await GetBasicFallbackResultAsync(postContent, claimTracking, cacheKey);
        }

        private string GetSpreadLevel(int repetitionCount)
        {
            if (repetitionCount >= 10) return "VIRAL";
            if (repetitionCount >= 6) return "HIGH";
            if (repetitionCount >= 3) return "MEDIUM";
            return "LOW";
        }

        private string GetRecommendedAction(int score)
        {
            if (score <= 2) return "Safe to share";
            if (score <= 4) return "Be cautious";
            if (score <= 6) return "Verify sources";
            if (score <= 8) return "Do not share";
            return "Report immediately";
        }

        private Task<FakeNewsAnalysisResult> GetBasicFallbackResultAsync(string postContent, ClaimTracking claimTracking, string cacheKey)
        {
            // Basic keyword analysis
            int score = 0;
            var flags = new List<string>();
            var analysis = new List<string>();

            var lowerText = postContent.ToLower();

            // Danger keywords (+2)
            if (lowerText.Contains("danger") || lowerText.Contains("urgent") || lowerText.Contains("evacuate") ||
                lowerText.Contains("disaster") || lowerText.Contains("emergency"))
            {
                score += 2;
                flags.Add("urgent");
                analysis.Add("Urgent/danger keywords detected");
            }

            // Unreliable language (+1)
            if (lowerText.Contains("i heard") || lowerText.Contains("people say") || lowerText.Contains("maybe") ||
                lowerText.Contains("not sure") || lowerText.Contains("rumor"))
            {
                score += 1;
                flags.Add("uncertain");
                analysis.Add("Uncertain language detected");
            }

            // Official language (-2)
            if (lowerText.Contains("official") || lowerText.Contains("confirmed") || lowerText.Contains("announced"))
            {
                score = Math.Max(0, score - 2);
                flags.Add("official");
                analysis.Add("Official/confirmed language detected");
            }

            // Repetition impact
            if (claimTracking.SimilarClaimsCount >= 3)
            {
                score += 1;
                flags.Add("repeated");
                analysis.Add($"High repetition ({claimTracking.SimilarClaimsCount} similar posts)");
            }

            // Clamp score
            score = Math.Clamp(score, 0, 10);

            var result = new FakeNewsAnalysisResult
            {
                Score = score,
                Label = score >= 7 ? "CRITICAL" : score >= 4 ? "HIGH" : score >= 2 ? "MEDIUM" : "LOW",
                Confidence = 60,
                Category = "General",
                Explanation = "Local analysis completed with claim tracking",
                Suggestions = new[] { "Verify information from credible sources" },
                ClaimTracking = claimTracking,
                RecommendedAction = score >= 7 ? "Do not share" : score >= 2 ? "Be cautious" : "Safe to share",
                Mode = "FALLBACK",
                Flags = flags.ToArray()
            };

            // Cache the result
            _cache.Set(cacheKey, result, TimeSpan.FromHours(24));
            return Task.FromResult(result);
        }
    }

    internal class AiAnalysisResult
    {
        public int Score { get; set; }
        public string? Label { get; set; }
        public int Confidence { get; set; }
        public string? Category { get; set; }
        public string? Explanation { get; set; }
        public string[]? Suggestions { get; set; }
    }

    public class FakeNewsAnalysisResult
    {
        public int Score { get; set; }
        public string? Label { get; set; }
        public int Confidence { get; set; }
        public string? Category { get; set; }
        public string? Explanation { get; set; }
        public string[]? Suggestions { get; set; }
        public ClaimTracking? ClaimTracking { get; set; }
        public string? RecommendedAction { get; set; }
        public string? Mode { get; set; } = "AI";
        public string[]? Flags { get; set; }
    }

    public class ClaimTracking
    {
        public bool IsRepeatedClaim { get; set; }
        public int SimilarClaimsCount { get; set; }
        public string? FirstSeen { get; set; }
        public string? SpreadLevel { get; set; }
    }

    internal class OpenAiResponse
    {
        public Choice[]? Choices { get; set; }

        public class Choice
        {
            public Message? Message { get; set; }
        }

        public class Message
        {
            public string? Content { get; set; }
        }
    }
}