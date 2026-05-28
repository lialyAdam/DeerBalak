using System;
using System.Collections.Generic;
using System.Linq;

namespace DeerBalak.Services
{
    public static class RiskAssessmentService
    {
        // Normalizes a parsed analysis result to enforce single-source-of-truth rules
        public static void Normalize(AiAnalysisResult result, bool lowConfidenceMode = false)
        {
            if (result == null) return;

            // Ensure Flags list
            result.Flags ??= new List<string>();

            // Normalize category mapping (avoid overuse of "Other")
            result.Category = MapCategory(result.Flags, result.Category);

            // Ensure a valid numeric score
            result.RiskScore = Math.Clamp(result.RiskScore, 0, 10);

            // Map numeric score to 5-level risk system
            var level = MapScoreToLevel(result.RiskScore);
            result.RiskLevel = level;

            // Recommendation text must follow RiskLevel only (no contradictions)
            result.RecommendedAction = RecommendationTextFromLevel(level);

            // Ensure main signals are present
            result.MainSignals = (result.MainSignals?.Any() ?? false) ? result.MainSignals : result.Flags.Take(5).ToList();

            // Low confidence (AI unavailable) handling: add badge and downgrade severity by at most one level
            if (lowConfidenceMode)
            {
                if (!result.Flags.Contains("low_confidence_mode"))
                    result.Flags.Add("low_confidence_mode");

                // downgrade one level maximum
                result.RiskLevel = DowngradeLevel(result.RiskLevel);
                result.RecommendedAction = RecommendationTextFromLevel(result.RiskLevel);

                // Make explanation explicit
                result.Explanation = (string.IsNullOrWhiteSpace(result.Explanation) ? string.Empty : result.Explanation + " ")
                                      + "Note: Low confidence mode — AI unavailable or fallback used.";
            }

            // Final safeguard: prevent contradictory outputs
            if (string.Equals(result.RiskLevel, "SAFE", StringComparison.OrdinalIgnoreCase) &&
                result.RecommendedAction?.StartsWith("Do not", StringComparison.OrdinalIgnoreCase) == true)
            {
                // override recommendation to safe text
                result.RecommendedAction = RecommendationTextFromLevel("SAFE");
            }
        }

        private static string MapCategory(IReadOnlyCollection<string> flags, string original)
        {
            var f = string.Join(" ", flags ?? Array.Empty<string>()).ToLowerInvariant();

            if (f.Contains("road") || f.Contains("traffic") || f.Contains("accident")) return "Traffic";
            if (f.Contains("danger") || f.Contains("bomb") || f.Contains("explosion") || f.Contains("attack")) return "Safety";
            if (f.Contains("evacuate") || f.Contains("evacuation")) return "Emergency";
            if (f.Contains("report") || f.Contains("news") || f.Contains("breaking")) return "News";

            // Prefer News over Other when unsure
            if (!string.IsNullOrWhiteSpace(original) && !original.Equals("Other", StringComparison.OrdinalIgnoreCase)) return original;

            return "News";
        }

        private static string MapScoreToLevel(int score)
        {
            if (score <= 2) return "SAFE";
            if (score <= 4) return "LOW RISK";
            if (score <= 6) return "MEDIUM RISK";
            if (score <= 8) return "HIGH RISK";
            return "CRITICAL";
        }

        private static string RecommendationTextFromLevel(string level)
        {
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

        private static string DowngradeLevel(string level)
        {
            return level switch
            {
                var s when s.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase) => "HIGH RISK",
                var s when s.Equals("HIGH RISK", StringComparison.OrdinalIgnoreCase) => "MEDIUM RISK",
                var s when s.Equals("MEDIUM RISK", StringComparison.OrdinalIgnoreCase) => "LOW RISK",
                var s when s.Equals("LOW RISK", StringComparison.OrdinalIgnoreCase) => "SAFE",
                _ => level
            };
        }
    }
}
