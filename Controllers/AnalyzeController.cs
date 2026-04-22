using Deerbalak.Data.Services;
using Microsoft.AspNetCore.Mvc;

namespace Deerbalak.Controllers
{
    [ApiController]
    [Route("api/analyze")]
    public class AnalyzeController : ControllerBase
    {
        private readonly AIService _aiService;

        public AnalyzeController(AIService aiService)
        {
            _aiService = aiService;
        }

        public class AnalyzeRequest
        {
            public string Text { get; set; } = string.Empty;
        }

        [HttpPost]
        public async Task<IActionResult> Analyze([FromBody] AnalyzeRequest request)
        {
            Console.WriteLine($"🔵 [AnalyzeController] Received request with text length: {request.Text?.Length ?? 0}");
            
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                Console.WriteLine("❌ [AnalyzeController] Text is empty");
                return BadRequest(new { error = "Text cannot be empty." });
            }

            var result = await _aiService.AnalyzeTextAsync(request.Text);
            
            Console.WriteLine($"✅ [AnalyzeController] Returning result: RiskScore={result.risk_score}, Label={result.label}, Confidence={result.confidence}, Mode={result.mode}");
            
            return Ok(result);
        }
    }
}
