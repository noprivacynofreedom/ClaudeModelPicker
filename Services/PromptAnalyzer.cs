using SharpToken;
using ClaudeModelPicker.Models;
using System.Text.RegularExpressions;

namespace ClaudeModelPicker.Services
{
    public class PromptAnalyzer
    {
        private static readonly string[] HaikuKeywords = { "summarize", "extract", "list", "fact", "code snippet", "quick", "simple" };
        private static readonly string[] SonnetKeywords = { "analyze", "compare", "design", "debug", "complex", "explain", "reason", "architecture" };

        public ModelPick Analyze(string prompt)
        {
            try
            {
                var tokenCount = CountTokens(prompt);
                var hasSonnetKeywords = HasKeywords(prompt, SonnetKeywords);
                var hasHaikuKeywords = HasKeywords(prompt, HaikuKeywords);

                // Scoring logic
                double sonnetScore = 0;
                double haikuScore = 0;

                // Token count scoring
                if (tokenCount < 150)
                    haikuScore += 0.4;
                else if (tokenCount > 300)
                    sonnetScore += 0.4;

                // Keyword scoring
                if (hasSonnetKeywords)
                    sonnetScore += 0.5;
                if (hasHaikuKeywords)
                    haikuScore += 0.4;

                // Normalize
                var total = sonnetScore + haikuScore;
                if (total == 0)
                {
                    return new ModelPick { PickedModel = "Haiku", Confidence = 0.5 };
                }

                sonnetScore /= total;
                haikuScore /= total;

                var picked = sonnetScore > haikuScore ? "Sonnet" : "Haiku";
                var confidence = Math.Max(sonnetScore, haikuScore);

                return new ModelPick
                {
                    PickedModel = picked,
                    Confidence = confidence,
                    TokenCount = tokenCount,
                    Reasoning = $"{picked} ({(int)(confidence * 100)}% confidence, {tokenCount} tokens)"
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Analysis error: {ex.Message}");
                return new ModelPick { PickedModel = "Haiku", Confidence = 0.5 };
            }
        }

        private int CountTokens(string text)
        {
            try
            {
                // Use cl100k_base encoding for Claude
                var encoding = GptEncoding.GetEncoding("cl100k_base");
                var tokens = encoding.Encode(text);
                return tokens.Count;
            }
            catch
            {
                // Fallback: rough estimate (1 token ≈ 4 chars)
                return text.Length / 4;
            }
        }

        private bool HasKeywords(string text, string[] keywords)
        {
            var lower = text.ToLower();
            return keywords.Any(kw => lower.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }
    }
}
