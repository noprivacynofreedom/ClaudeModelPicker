using SharpToken;
using ClaudeModelPicker.Models;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Scores a prompt and recommends Haiku, Sonnet or Opus, based on token count
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

        public ModelPick Analyze(string? prompt)
        {
            try
            {
                prompt ??= string.Empty;
                var tokenCount = CountTokens(prompt);
                var tokenWeight = _config.AnalysisTokenCountWeight;
                var keywordWeight = _config.AnalysisKeywordsWeight;

                // Index order = ModelNames.Families: Haiku, Sonnet, Opus.
                var scores = new double[3];

                if (tokenCount < _config.AnalysisHaikuMaxTokens)
                    scores[0] += tokenWeight;
                else if (tokenCount > _config.AnalysisOpusMinTokens)
                    scores[2] += tokenWeight;
                else if (tokenCount > _config.AnalysisSonnetMinTokens)
                    scores[1] += tokenWeight;

                if (HasKeywords(prompt, _config.AnalysisHaikuKeywords)) scores[0] += keywordWeight;
                if (HasKeywords(prompt, _config.AnalysisSonnetKeywords)) scores[1] += keywordWeight;
                if (HasKeywords(prompt, _config.AnalysisOpusKeywords)) scores[2] += keywordWeight;

                var total = scores.Sum();
                if (total == 0)
                {
                    return new ModelPick { PickedModel = "Haiku", Confidence = 0.5, TokenCount = tokenCount };
                }

                // Strict ">" so a tie goes to the smaller (cheaper) model.
                var best = 0;
                for (var i = 1; i < scores.Length; i++)
                    if (scores[i] > scores[best]) best = i;

                var picked = ModelNames.Families[best];
                var confidence = scores[best] / total;

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

        /// <summary>
        /// Whole-word, case-insensitive keyword match. Plain substring matching
        /// made "refactor" hit the Haiku keyword "fact" and "listen" hit "list".
        /// Simple plural/past forms still match ("lists", "summarized").
        /// </summary>
        public static bool HasKeywords(string text, string[] keywords)
        {
            if (keywords.Length == 0 || string.IsNullOrEmpty(text)) return false;
            return keywords.Any(kw => !string.IsNullOrWhiteSpace(kw) &&
                Regex.IsMatch(text, @"(?<!\w)" + Regex.Escape(kw.Trim()) + @"(s|es|d|ed)?(?!\w)", RegexOptions.IgnoreCase));
        }
    }
}
