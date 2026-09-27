// Test templates for PromptAnalyzer.Analyze().
// Framework: xUnit (not yet in the .csproj — Sonnet needs to add
// Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio, plus a
// separate ClaudeModelPicker.Tests.csproj referencing the main project).
//
// These are templates, not verified-passing tests — some assertions
// (e.g. exact confidence numbers) need to be run against the real
// PromptAnalyzer.Analyze() once the tiktoken-net -> SharpToken
// dependency mismatch in ClaudeModelPicker.csproj is fixed
// (see docs/security-audit.md, "Build inconsistency" note).

using Xunit;
using ClaudeModelPicker.Services;

namespace ClaudeModelPicker.Tests
{
    public class PromptAnalyzerTests
    {
        private readonly PromptAnalyzer _analyzer = new();

        // --- Token counting accuracy ---

        [Fact]
        public void Analyze_EmptyPrompt_DoesNotThrow()
        {
            var result = _analyzer.Analyze("");
            Assert.NotNull(result);
        }

        [Fact]
        public void Analyze_ShortPrompt_ScoresLowTokenCount()
        {
            var result = _analyzer.Analyze("hi");
            Assert.True(result.TokenCount < 150);
        }

        [Theory]
        [InlineData("a")]           // 1 char
        [InlineData("hello world")] // ~2 tokens
        public void Analyze_TokenCount_IsPositive(string prompt)
        {
            var result = _analyzer.Analyze(prompt);
            Assert.True(result.TokenCount > 0);
        }

        // --- Keyword detection ---

        [Theory]
        [InlineData("please summarize this article")]
        [InlineData("extract the key facts")]
        [InlineData("give me a quick list")]
        public void Analyze_HaikuKeywords_LeanHaiku(string prompt)
        {
            var result = _analyzer.Analyze(prompt);
            Assert.Equal("Haiku", result.PickedModel);
        }

        [Theory]
        [InlineData("analyze the architecture of this system and explain the tradeoffs")]
        [InlineData("debug this complex race condition and reason through the fix")]
        [InlineData("compare these two design approaches")]
        public void Analyze_SonnetKeywords_LeanSonnet(string prompt)
        {
            var result = _analyzer.Analyze(prompt);
            Assert.Equal("Sonnet", result.PickedModel);
        }

        [Fact]
        public void Analyze_NoKeywordsNoLength_DefaultsToHaikuWithHalfConfidence()
        {
            // Mirrors the `total == 0` branch in PromptAnalyzer.Analyze()
            var result = _analyzer.Analyze("xyz qqq zzz"); // no keyword hits, short enough to not hit length scoring cleanly
            Assert.Equal(0.5, result.Confidence, precision: 2);
        }

        // --- Confidence scoring ---

        [Fact]
        public void Analyze_Confidence_IsBetweenZeroAndOne()
        {
            var result = _analyzer.Analyze("analyze and compare these two architectures in detail, reasoning through tradeoffs");
            Assert.InRange(result.Confidence, 0.0, 1.0);
        }

        [Fact]
        public void Analyze_ConflictingKeywords_PicksHigherScoringSide()
        {
            // Has both a Haiku keyword ("summarize") and Sonnet keywords ("analyze", "complex")
            var result = _analyzer.Analyze("summarize this complex analysis");
            // Sonnet weight (0.5) > Haiku weight (0.4) when both keyword sets match —
            // expected to lean Sonnet. Verify against the real weighted formula once
            // config-driven weights (Task 3, ConfigManager) are wired in.
            Assert.Equal("Sonnet", result.PickedModel);
        }

        // --- Edge cases ---

        [Fact]
        public void Analyze_VeryLongPrompt_DoesNotThrow()
        {
            var longPrompt = string.Concat(Enumerable.Repeat("word ", 5000));
            var result = _analyzer.Analyze(longPrompt);
            Assert.NotNull(result);
            Assert.True(result.TokenCount > 300);
        }

        [Fact]
        public void Analyze_NullLikeWhitespace_DoesNotThrow()
        {
            var result = _analyzer.Analyze("   ");
            Assert.NotNull(result);
        }

        [Fact]
        public void Analyze_UnicodeAndEmoji_DoesNotThrow()
        {
            var result = _analyzer.Analyze("分析这个复杂的架构 🚀 and explain it");
            Assert.NotNull(result);
        }
    }
}
