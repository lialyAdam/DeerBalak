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
            // Enhanced keyword analysis with intelligent scoring
            int score = 0;
            var flags = new List<string>();
            var analysis = new List<string>();

            var lowerText = postContent.ToLower();
            var hasAllCaps = postContent.Any(char.IsUpper) && postContent.Where(char.IsLetter).Count(char.IsUpper) > postContent.Length / 4;

            // CRITICAL danger keywords (+4 each)
            var criticalKeywords = new[] { "evacuate", "evacuate now", "everyone evacuate", "immediately evacuate", 
                                          "stay inside", "everyone stay inside", "bomb", "bombing", "explosion",
                                          "attack", "under attack", "disaster", "catastrophe", "apocalypse" };
            foreach (var keyword in criticalKeywords)
            {
                if (lowerText.Contains(keyword))
                {
                    score += 4;
                    flags.Add("critical_danger");
                    analysis.Add($"Critical alert keyword detected: '{keyword}'");
                    break; // Count once
                }
            }

            // HIGH risk keywords (+3 each)
            var highRiskKeywords = new[] { "danger", "dangerous", "unsafe", "emergency", "urgent", "immediately",
                                          "right now", "everyone", "all areas", "must", "critical", "alert",
                                          "warning", "beware", "avoid", "spread", "panic" };
            var highRiskCount = 0;
            foreach (var keyword in highRiskKeywords)
            {
                if (lowerText.Contains(keyword))
                {
                    highRiskCount++;
                }
            }
            if (highRiskCount > 0)
            {
                score += Math.Min(3, highRiskCount); // Up to +3 for multiple risk keywords
                if (highRiskCount >= 2)
                {
                    flags.Add("multiple_risk_signals");
                    analysis.Add($"Multiple risk signals detected ({highRiskCount} keywords)");
                }
            }

            // Unreliable language (+1)
            if (lowerText.Contains("i heard") || lowerText.Contains("people say") || lowerText.Contains("maybe") ||
                lowerText.Contains("not sure") || lowerText.Contains("rumor") || lowerText.Contains("allegedly"))
            {
                score += 1;
                flags.Add("uncertain");
                analysis.Add("Uncertain/hearsay language detected");
            }

            // ALL CAPS usage (+2 if combined with other risk factors)
            if (hasAllCaps && score > 0)
            {
                score += 2;
                flags.Add("all_caps_emphasis");
                analysis.Add("All-caps emphasis combined with risk keywords");
            }

            // No credible sources (-1 if risky claim lacks attribution)
            if (score > 3 && !lowerText.Contains("according to") && !lowerText.Contains("source") && 
                !lowerText.Contains("confirmed") && !lowerText.Contains("official"))
            {
                score += 1;
                flags.Add("no_sources");
                analysis.Add("Risky claim without source attribution");
            }

            // Official language (-2)
            if (lowerText.Contains("official") || lowerText.Contains("confirmed") || lowerText.Contains("announced") ||
                lowerText.Contains("according to official"))
            {
                score = Math.Max(0, score - 2);
                flags.Add("official");
                analysis.Add("Official/confirmed language detected");
            }

            // Repetition impact (higher weight than before)
            if (claimTracking.SimilarClaimsCount >= 10)
            {
                score += 3;
                flags.Add("viral_spread");
                analysis.Add($"Viral spread detected ({claimTracking.SimilarClaimsCount} similar posts)");
            }
            else if (claimTracking.SimilarClaimsCount >= 5)
            {
                score += 2;
                flags.Add("repeated_high");
                analysis.Add($"High repetition ({claimTracking.SimilarClaimsCount} similar posts)");
            }
            else if (claimTracking.SimilarClaimsCount >= 3)
            {
                score += 1;
                flags.Add("repeated");
                analysis.Add($"Repeated claim ({claimTracking.SimilarClaimsCount} similar posts)");
            }

            // Final score adjustment and clamping
            score = Math.Clamp(score, 0, 10);

            // Intelligent labeling based on actual risk
            string label;
            if (score >= 8) label = "CRITICAL";
            else if (score >= 6) label = "HIGH RISK";
            else if (score >= 4) label = "MEDIUM RISK";
            else if (score >= 2) label = "LOW RISK";
            else label = "SAFE";

            var result = new FakeNewsAnalysisResult
            {
                Score = score,
                Label = label,
                Confidence = score > 0 ? 75 : 0, // Higher confidence for detected risks
                Category = DetermineCategoryFromFlags(flags),
                Explanation = string.Join("; ", analysis.Any() ? analysis : new[] { "Local keyword analysis completed" }) + 
                            (claimTracking.IsRepeatedClaim ? $"; Claim has appeared {claimTracking.SimilarClaimsCount} times" : ""),
                Suggestions = GenerateSuggestions(score, claimTracking.SimilarClaimsCount),
                ClaimTracking = claimTracking,
                RecommendedAction = GetRecommendedActionForScore(score),
                Mode = "FALLBACK (Local Analysis)",
                Flags = flags.ToArray()
            };

            // Cache the result
            _cache.Set(cacheKey, result, TimeSpan.FromHours(24));
            return Task.FromResult(result);
        }

        private string DetermineCategoryFromFlags(List<string> flags)
        {
            if (flags.Contains("critical_danger") || flags.Contains("all_caps_emphasis")) return "Safety Alert";
            if (flags.Contains("viral_spread") || flags.Contains("repeated_high")) return "Misinformation Spread";
            if (flags.Contains("uncertain") || flags.Contains("no_sources")) return "Unverified Claim";
            return "General Alert";
        }

        private string[] GenerateSuggestions(int score, int repetitionCount)
        {
            var suggestions = new List<string>();

            if (score >= 8)
            {
                suggestions.Add("⚠️ Avoid sharing immediately");
                suggestions.Add("Verify with official sources");
                if (repetitionCount >= 5) suggestions.Add("Report for potential misinformation");
            }
            else if (score >= 6)
            {
                suggestions.Add("Check credible news sources");
                suggestions.Add("Verify before sharing");
            }
            else if (score >= 4)
            {
                suggestions.Add("Verify information from credible sources");
            }
            else if (score >= 2)
            {
                suggestions.Add("Be cautious before sharing");
            }
            else
            {
                suggestions.Add("Content appears safe to share");
            }

            return suggestions.ToArray();
        }

        private string GetRecommendedActionForScore(int score)
        {
            if (score >= 8) return "⛔ Critical: Do not share";
            if (score >= 6) return "🔴 High Risk: Do not share";
            if (score >= 4) return "🟠 Medium Risk: Verify before sharing";
            if (score >= 2) return "🟡 Low Risk: Be cautious";
            return "🟢 Safe to share";
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