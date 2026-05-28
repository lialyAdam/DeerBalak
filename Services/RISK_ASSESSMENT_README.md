Risk Assessment integration notes
================================

What I added
------------
- `Services/RiskAssessmentService.cs`: static normalizer that enforces single final `RiskLevel`, maps numeric score to a 5-level system, produces consistent recommendation texts, applies an AI-unavailable "low confidence" downgrade, and improves category mapping.

OpenAIService changes
---------------------
- `Services/OpenAIService.cs` now:
  - Detects simple repetition phrases ("appeared X times") and applies a small boost up to +0.5 to the internal score.
  - Uses double internal scoring and rounds to integer final `risk_score`.
  - Calls `RiskAssessmentService.Normalize(...)` for both AI and fallback results to remove contradictions and standardize outputs.

Integration / Notes
-------------------
- No DI registration required (the normalizer is static). `IOpenAIService` is still registered in `Program.cs`.
- If you'd prefer DI for the normalizer, convert it to an injectable service and register it in `Program.cs`.

Recommended next steps
----------------------
- Run the app and exercise the `AiController` endpoints to ensure outputs are as expected.
- Optional: add unit tests for `RiskAssessmentService.Normalize(...)` covering downgrade and mapping rules.

Example: how to call (controller already wired)

1. Start the app

```powershell
dotnet run
```

2. Call the AI analyze endpoint (example)

```powershell
curl -X POST "https://localhost:5001/api/ai/analyze" -H "Content-Type: application/json" -d '{"text":"There is a dangerous evacuation needed. Appeared 3 times."}'
```

The response will include `risk_score`, `risk_level`, `confidence`, `recommended_action` (text), and `flags` including `low_confidence_mode` when AI is unavailable.
