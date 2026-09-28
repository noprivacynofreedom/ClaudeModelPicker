using SharpToken;
using ClaudeModelPicker.Models;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Scores a prompt and recommends Haiku or Sonnet, based on token count
    /// and keyword matches. All thresholds/keywords come from ConfigManager
    /// (config.json) rather than being hardcoded, so tuning doesn't require
    /// a rebuild.
    /// </summary>
    public class PromptAnalyzer
    {
        private readonly ConfigManager _config;
        private readonly GptEncoding _encoding;

        // Cached: loading cl100k_base takes ~500ms on first use (see README
        // "Known Limitations" / "High latency on Enter"). Loading it once in
        // the constructor instead of per-call keeps the hook path fast after
        // startup.
        public PromptAnalyzer(ConfigManager config)
        {
            _config = config;
            _encoding = GptEncoding.GetEncoding("cl100k_base");
        }

        // Credential-shaped patterns stripped before a prompt is ever logged
        // or stored. Best-effort, not exhaustive — see SECURITY-AUDIT.md
        // Finding #1. Applied here (at analysis time) so every caller gets a
        // sanitized ModelPickLog for free, rather than relying on each log
        // call site to remember to sanitize.
        private static readonly (string Pattern, string Replacement)[] SanitizePatterns =
        {
            (@"sk-\w{20,}", "[REDACTED]"),           // OpenAI/Anthropic-style keys
            (@"[A-Z0-9]{20,}", "[REDACTED]"),         // generic long uppercase tokens (AWS-style)
            (@"password\s*[:=]\s*\S+", "password=[REDACTED]"),
            (@"token\s*[:=]\s*\S+", "token=[REDACTED]"),
            (@"secret\s*[:=]\s*\S+", "secret=[REDACTED]"),
            (@"Bearer\s+[A-Za-z0-9\-_.]{10,}", "Bearer [REDACTED]"),
        };

        public static string SanitizePrompt(string prompt)
        {
            var sanitized = prompt;
            foreach (var (pattern, replacement) in SanitizePatterns)
                sanitized = Regex.Replace(sanitized, pattern, replacement, RegexOptions.IgnoreCase);
            return sanitized;
        }

        public ModelPick Analyze(string prompt)
        {
            try
            {
                var tokenCount = CountTokens(prompt);
                var haikuKeywords = _config.AnalysisHaikuKeywords;
                var sonnetKeywords = _config.AnalysisSonnetKeywords;

                var hasHaikuKeywords = HasKeywords(prompt, haikuKeywords);
                var hasSonnetKeywords = HasKeywords(prompt, sonnetKeywords);

                double sonnetScore = 0;
                double haikuScore = 0;

                if (tokenCount < _config.AnalysisHaikuMaxTokens)
                    haikuScore += _config.AnalysisTokenCountWeight;
                if (tokenCount > _config.AnalysisSonnetMinTokens)
                    sonnetScore += _config.AnalysisTokenCountWeight;

                if (hasSonnetKeywords)
                    sonnetScore += _config.AnalysisKeywordsWeight;
                if (hasHaikuKeywords)
                    haikuScore += _config.AnalysisKeywordsWeight;

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
            catch (Exception)
            {
                return new ModelPick { PickedModel = "Haiku", Confidence = 0.5 };
            }
        }

        private int CountTokens(string text)
        {
            try
            {
                return _encoding.Encode(text).Count;
            }
            catch
            {
                return text.Length / 4; // rough fallback: ~4 chars/token
            }
        }

        private static bool HasKeywords(string text, string[] keywords)
        {
            if (keywords.Length == 0) return false;
            var lower = text.ToLowerInvariant();
            return keywords.Any(kw => lower.Contains(kw.ToLowerInvariant()));
        }
    }
}
