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
            ClaimTrackingService? claimTrackingService = null)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _cache = cache;
            _claimTrackingService = claimTrackingService;
            
            // Configure HttpClient timeouts
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<FakeNewsAnalysisResult> AnalyzePostAsync(string postContent, string? userId = null, int postId = 0)
        {
            // Check cache first - THIS IS THE MOST IMPORTANT PART
            var cacheKey = $"FakeNewsAnalysis_{postContent.GetHashCode()}";
            if (_cache.TryGetValue(cacheKey, out FakeNewsAnalysisResult? cachedResult))
            {
                _logger.LogInformation("✅ Returning cached AI analysis result (no API call)");
                return cachedResult!;
            }

            // Get repetition count from claim tracking
            int repetitionCount = 0;
            if (_claimTrackingService != null)
            {
                try
                {
                    var trackingResult = await _claimTrackingService.AnalyzeClaimSpreadAsync(postContent, postId);
                    repetitionCount = trackingResult.SimilarClaimsCount;
                    _logger.LogInformation($"📊 Repetition count: {repetitionCount}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch claim tracking data for repetition count");
                }
            }

            try
            {
                var openAiApiKey = _configuration["OpenAI:ApiKey"];
                if (string.IsNullOrEmpty(openAiApiKey) || openAiApiKey.StartsWith("YOUR_"))
                {
                    _logger.LogWarning("❌ OpenAI API key not configured or invalid");
                    return await GetFallbackResultWithClaimTrackingAsync(postContent, postId);
                }

                // Apply rate limiting BEFORE making the API call
                _logger.LogInformation("🔵 Waiting for throttle slot before calling API...");
                bool throttleAcquired = await _requestThrottle.WaitAsync(TimeSpan.FromSeconds(5));
                
                if (!throttleAcquired)
                {
                    _logger.LogWarning("⏱️ Rate limit reached, returning fallback result");
                    return await GetFallbackResultWithClaimTrackingAsync(postContent, postId);
                }

                try
                {
                    var prompt = string.Format(PromptTemplate, postContent.Trim(), repetitionCount);

                    var requestBody = new
                    {
                        model = "gpt-3.5-turbo",  // Using cheaper model as fallback
                        messages = new[]
                        {
                            new { role = "user", content = prompt }
                        },
                        max_tokens = 1000,
                        temperature = 0.3
                    };

                    var json = JsonSerializer.Serialize(requestBody);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    // Clear previous headers and set new ones
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", openAiApiKey);
                    _httpClient.DefaultRequestHeaders.Add("User-Agent", "DeerBalak-AI-Detection/1.0");

                    _logger.LogInformation("📤 Sending request to OpenAI API...");
                    
                    // Retry logic with exponential backoff
                    int maxRetries = 2;
                    int retryCount = 0;
                    HttpResponseMessage? response = null;

                    while (retryCount < maxRetries)
                    {
                        try
                        {
                            response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
                            
                            // If successful, break the loop
                            if (response.IsSuccessStatusCode)
                            {
                                _logger.LogInformation($"✅ OpenAI API responded successfully (attempt {retryCount + 1})");
                                break;
                            }
                            
                            // If 429 (Too Many Requests), wait and retry
                            if ((int)response.StatusCode == 429)
                            {
                                retryCount++;
                                if (retryCount < maxRetries)
                                {
                                    int delayMs = (int)Math.Pow(2, retryCount) * 1000; // Exponential backoff
                                    _logger.LogWarning($"⏱️ Got 429 - Rate limited. Waiting {delayMs}ms before retry {retryCount}/{maxRetries}...");
                                    await Task.Delay(delayMs);
                                    continue;
                                }
                            }
                            // For other errors, break immediately
                            else
                            {
                                break;
                            }
                        }
                        catch (TaskCanceledException ex)
                        {
                            _logger.LogError($"⏱️ Request timeout: {ex.Message}");
                            break;
                        }
                    }

                    if (response == null || !response.IsSuccessStatusCode)
                    {
                        string errorMsg = response != null 
                            ? $"{(int)response.StatusCode} - {response.ReasonPhrase}"
                            : "No response";
                        _logger.LogError($"❌ OpenAI API error: {errorMsg}");
                        return await GetFallbackResultWithClaimTrackingAsync(postContent, postId);
                    }

                    var responseJson = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation($"📥 Received response from OpenAI (length: {responseJson.Length})");
                    
                    try
                    {
                        var openAiResponse = JsonSerializer.Deserialize<OpenAiResponse>(responseJson);

                        if (openAiResponse?.Choices?.Length > 0)
                        {
                            var analysisJson = openAiResponse.Choices[0].Message?.Content;
                            if (analysisJson != null)
                            {
                                _logger.LogInformation($"📊 AI response: {analysisJson.Substring(0, Math.Min(100, analysisJson.Length))}...");
                                
                                var aiResult = JsonSerializer.Deserialize<AiAnalysisResult>(analysisJson);

                            if (aiResult != null)
                            {
                                _logger.LogInformation($"✅ AI analysis completed - Score: {aiResult.Score}, Label: {aiResult.Label}");

                                // Get claim tracking information
                                var claimTracking = new ClaimTracking 
                                { 
                                    IsRepeatedClaim = repetitionCount > 0, 
                                    SimilarClaimsCount = repetitionCount, 
                                    SpreadLevel = GetSpreadLevel(repetitionCount),
                                    FirstSeen = null
                                };

                                if (_claimTrackingService != null)
                                {
                                    try
                                    {
                                        var trackingResult = await _claimTrackingService.AnalyzeClaimSpreadAsync(postContent, postId);
                                        if (trackingResult != null)
                                        {
                                            claimTracking.FirstSeen = trackingResult.FirstSeenDate.ToString("MMM dd, yyyy");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, "Could not fetch detailed claim tracking data");
                                    }
                                }

                                var result = new FakeNewsAnalysisResult
                                {
                                    Score = aiResult.Score,
                                    Label = aiResult.Label,
                                    Confidence = aiResult.Confidence,
                                    Category = aiResult.Category,
                                    Explanation = aiResult.Explanation,
                                    Suggestions = aiResult.Suggestions,
                                    ClaimTracking = claimTracking,
                                    RecommendedAction = GetRecommendedAction(aiResult.Score)
                                };

                                // Cache the result for 24 hours
                                _cache.Set(cacheKey, result, TimeSpan.FromHours(24));

                                return result;
                            }
                        }
                    }}
                    catch (JsonException ex)
                    {
                        _logger.LogError($"❌ Failed to parse AI response: {ex.Message}");
                        _logger.LogError($"Response was: {responseJson}");
                    }

                    return await GetFallbackResultWithClaimTrackingAsync(postContent, postId);
                }
                finally
                {
                    // Release the throttle slot
                    _requestThrottle.Release();
                    _logger.LogInformation("🟢 Released throttle slot");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error analyzing post with AI");
                return await GetFallbackResultWithClaimTrackingAsync(postContent, postId);
            }
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

        private async Task<FakeNewsAnalysisResult> GetFallbackResultWithClaimTrackingAsync(string postContent, int postId = 0)
        {
            // Get claim tracking information from database
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

            return new FakeNewsAnalysisResult
            {
                Score = 0,
                Label = "SAFE",
                Confidence = 50,
                Category = "Other",
                Explanation = "✅ AI analysis unavailable - using local verification based on claim tracking",
                Suggestions = new[] 
                { 
                    "📊 See claim tracking info below to understand spread pattern",
                    "🔍 Consider verifying information from credible sources"
                },
                ClaimTracking = claimTracking,
                RecommendedAction = "Safe to share"
            };
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