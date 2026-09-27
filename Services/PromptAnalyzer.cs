using SharpToken;
using ClaudeModelPicker.Models;
using System;
using System.Linq;

namespace ClaudeModelPicker.Services
{
    public class PromptAnalyzer
    {
        // Used only if no ConfigManager is supplied (e.g. unit tests).
        private static readonly string[] DefaultHaikuKeywords = { "summarize", "extract", "list", "fact", "code snippet", "quick", "simple" };
        private static readonly string[] DefaultSonnetKeywords = { "analyze", "compare", "design", "debug", "complex", "explain", "reason", "architecture" };
        private const int DefaultHaikuMaxTokens = 150;
        private const int DefaultSonnetMinTokens = 300;

        private readonly string[] _haikuKeywords;
        private readonly string[] _sonnetKeywords;
        private readonly int _haikuMaxTokens;
        private readonly int _sonnetMinTokens;

        public PromptAnalyzer(ConfigManager? config = null)
        {
            _haikuKeywords = config?.AnalysisHaikuKeywords is { Length: > 0 } hk ? hk : DefaultHaikuKeywords;
            _sonnetKeywords = config?.AnalysisSonnetKeywords is { Length: > 0 } sk ? sk : DefaultSonnetKeywords;
            _haikuMaxTokens = config?.AnalysisHaikuMaxTokens ?? DefaultHaikuMaxTokens;
            _sonnetMinTokens = config?.AnalysisSonnetMinTokens ?? DefaultSonnetMinTokens;
        }

        public ModelPick Analyze(string prompt)
        {
            try
            {
                var tokenCount = CountTokens(prompt);
                var hasSonnetKeywords = HasKeywords(prompt, _sonnetKeywords);
                var hasHaikuKeywords = HasKeywords(prompt, _haikuKeywords);

                double sonnetScore = 0;
                double haikuScore = 0;

                if (tokenCount < _haikuMaxTokens)
                    haikuScore += 0.4;
                else if (tokenCount > _sonnetMinTokens)
                    sonnetScore += 0.4;

                if (hasSonnetKeywords)
                    sonnetScore += 0.5;
                if (hasHaikuKeywords)
                    haikuScore += 0.4;

                var total = sonnetScore + haikuScore;
                if (total == 0)
                {
                    return new ModelPick { PickedModel = "Haiku", Confidence = 0.5, TokenCount = tokenCount };
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
                var encoding = GptEncoding.GetEncoding("cl100k_base");
                var tokens = encoding.Encode(text);
                return tokens.Count;
            }
            catch
            {
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
