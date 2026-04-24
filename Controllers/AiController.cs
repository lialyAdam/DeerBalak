using System.ComponentModel.DataAnnotations;
using DeerBalak.Services;
using Microsoft.AspNetCore.Mvc;

namespace DeerBalak.Controllers
{
    /// <summary>
    /// AI Analysis API Controller for detecting misinformation and content safety
    /// </summary>
    [ApiController]
    [Route("api/ai")]
    public class AiController : ControllerBase
    {
        private readonly IOpenAIService _openAIService;
        private readonly ILogger<AiController> _logger;

        public AiController(IOpenAIService openAIService, ILogger<AiController> logger)
        {
            _openAIService = openAIService;
            _logger = logger;
        }

        /// <summary>
        /// Analyze a post for misinformation, fake news, and safety risks
        /// </summary>
        /// <param name="request">The post content to analyze</param>
        /// <returns>Detailed analysis result with risk assessment</returns>
        [HttpPost("analyze")]
        [ProducesResponseType(typeof(AiAnalysisResult), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> Analyze([FromBody] AnalyzeRequest request)
        {
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(request.Text))
            {
                _logger.LogWarning("Invalid analyze request: ModelState={ModelState}, Text={Text}",
                    ModelState.IsValid, request.Text?.Length ?? 0);
                return BadRequest(new { error = "Valid text content is required for analysis." });
            }

            try
            {
                _logger.LogInformation("🔍 Starting AI analysis for post with {Length} characters",
                    request.Text.Length);

                var result = await _openAIService.AnalyzePostAsync(request.Text.Trim());

                _logger.LogInformation("✅ Analysis completed: RiskScore={RiskScore}, Confidence={Confidence}%, Category={Category}",
                    result.RiskScore, result.Confidence, result.Category);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during AI analysis");
                return StatusCode(500, new { error = "Internal server error during analysis" });
            }
        }

        /// <summary>
        /// Health check endpoint for AI service status
        /// </summary>
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                service = "OpenAI Analysis Service",
                version = "1.0.0"
            });
        }

        /// <summary>
        /// Request model for post analysis
        /// </summary>
        public sealed class AnalyzeRequest
        {
            /// <summary>
            /// The text content to analyze for misinformation
            /// </summary>
            [Required(ErrorMessage = "Text content is required")]
            [StringLength(10000, MinimumLength = 1, ErrorMessage = "Text must be between 1 and 10000 characters")]
            public string Text { get; set; } = string.Empty;
        }
    }
}
