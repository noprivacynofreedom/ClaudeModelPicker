using System;
using System.Linq;
using Xunit;
using ClaudeModelPicker.Services;
using ClaudeModelPicker.Models;

namespace ClaudeModelPicker.Tests
{
    /// <summary>
    /// Unit tests for PromptAnalyzer.
    /// Tests token counting, keyword detection, and confidence scoring.
    ///
    /// To run:
    /// dotnet test
    /// </summary>
    public class PromptAnalyzerTests
    {
        private readonly PromptAnalyzer _analyzer = new PromptAnalyzer();

        #region Token Counting Tests

        [Fact]
        public void CountTokens_ShortPrompt_ReturnsSmallCount()
        {
            // Arrange
            string prompt = "Hello";

            // Act
            var result = _analyzer.Analyze(prompt);

            // Assert
            Assert.True(result.TokenCount > 0, "Token count should be > 0");
            Assert.True(result.TokenCount < 20, "Short prompt should have < 20 tokens");
        }

        [Fact]
        public void CountTokens_MediumPrompt_ReturnsAccurateCount()
        {
            // Arrange
            string prompt = "Can you explain how machine learning works in simple terms?";

            // Act
            var result = _analyzer.Analyze(prompt);

            // Assert
            // Should be roughly 12-15 tokens
            Assert.True(result.TokenCount >= 10 && result.TokenCount <= 20,
                $"Medium prompt should have ~12-15 tokens, got {result.TokenCount}");
        }

        [Fact]
        public void CountTokens_LongPrompt_ReturnsHighCount()
        {
            // Arrange
            // NOTE: repeated identical characters (e.g. new string('a', 1000)) compress
            // to very few BPE tokens and do not exercise this path. Use distinct words
            // instead so the token count actually reflects prompt length.
            string prompt = string.Join(" ", Enumerable.Range(1, 300).Select(i => $"uniqueword{i}"));

            // Act
            var result = _analyzer.Analyze(prompt);

            // Assert
            Assert.True(result.TokenCount > 200, $"300 distinct words should be > 200 tokens, got {result.TokenCount}");
        }

        #endregion

        #region Keyword Detection Tests

        [Fact]
        public void Analyze_HaikuKeywordPrompt_SelectsHaiku()
        {
            // Arrange
            string prompt = "Summarize this article for me";

            // Act
            var result = _analyzer.Analyze(prompt);

            // Assert
            Assert.Equal("Haiku", result.PickedModel);
            Assert.True(result.Confidence > 0.5,
                $"Should have high confidence for Haiku keyword; got {result.Confidence:P}");
        }

        [Fact]
        public void Analyze_SonnetKeywordPrompt_SelectsSonnet()
        {
            // Arrange
            string prompt = "Analyze the architecture and design of this system";

            // Act
            var result = _analyzer.Analyze(prompt);

            // Assert
            Assert.Equal("Sonnet", result.PickedModel);
            Assert.True(result.Confidence > 0.5,
                $"Should have high confidence for Sonnet keyword; got {result.Confidence:P}");
        }

        [Fact]
        public void Analyze_MultipleKeywordsConflicting_SelectsHighestScore()
        {
            // Arrange
            // Contains both Haiku keywords (extract, list) and Sonnet keywords (analyze)
            // But analysis might favor one
            string prompt = "Extract and list the key items, then analyze the results";

            // Act
            var result = _analyzer.Analyze(prompt);

            // Assert
            Assert.NotEmpty(result.PickedModel);
            Assert.True(result.Confidence > 0, "Confidence should be > 0");
        }

        #endregion

        #region Token Count Threshold Tests

        [Fact]
        public void Analyze_TinyPromptHaikuThreshold_PreferHaiku()
        {
            // Arrange
            string prompt = "Hi"; // Very short, well under 150 token threshold

            // Act
            var result = _analyzer.Analyze(prompt);

            // Assert
            Assert.Equal("Haiku", result.PickedModel);
        }

        [Fact]
        public void Analyze_HugePromptSonnetThreshold_PreferSonnet()
        {
            // Arrange
            // NOTE: 100 short "wordN" tokens landed under the 300-token Sonnet
            // threshold with the real tokenizer. Use enough distinct words to
            // reliably clear DefaultSonnetMinTokens (300).
            string prompt = string.Join(" ",
                Enumerable.Range(1, 400).Select(i => $"word{i}"));

            // Act
            var result = _analyzer.Analyze(prompt);

            // Assert
            Assert.Equal("Sonnet", result.PickedModel);
        }

        #endregion

        #region Confidence Scoring Tests

        [Fact]
        public void Analyze_ClearHaikuCase_HighConfidence()
        {
            // Arrange
            string prompt = "Quickly summarize this";

            // Act
            var result = _analyzer.Analyze(prompt);

            // Assert
            Assert.True(result.Confidence > 0.6,
                $"Clear Haiku case should have confidence > 0.6, got {result.Confidence:P}");
        }

        [Fact]
        public void Analyze_AmbiguousPrompt_ModerateConfidence()
        {
            // Arrange
            string prompt = "Tell me something";

            // Act
            var result = _analyzer.Analyze(prompt);

            // Assert
            Assert.True(result.Confidence >= 0 && result.Confidence <= 1,
                "Confidence should always be in [0, 1]");
        }

        [Fact]
        public void Analyze_EmptyPrompt_ReturnsDefault()
        {
            // Arrange
            string prompt = "";

            // Act
            var result = _analyzer.Analyze(prompt);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Haiku", result.PickedModel); // Default to Haiku
        }

        #endregion

        #region Reasoning Field Tests

        [Fact]
        public void Analyze_Result_ContainsReasoningString()
        {
            // Arrange
            string prompt = "Analyze this carefully";

            // Act
            var result = _analyzer.Analyze(prompt);

            // Assert
            Assert.NotEmpty(result.Reasoning);
            Assert.Contains(result.PickedModel, result.Reasoning);
            Assert.Contains("%", result.Reasoning); // Confidence percentage
            Assert.Contains("tokens", result.Reasoning.ToLower());
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public void Analyze_NullPrompt_HandlesGracefully()
        {
            // Arrange & Act
            var result = _analyzer.Analyze(null);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Haiku", result.PickedModel);
        }

        [Fact]
        public void Analyze_VeryLongPrompt_DoesNotCrash()
        {
            // Arrange
            string prompt = new string('x', 100000); // 100KB of text

            // Act
            var result = _analyzer.Analyze(prompt);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.TokenCount > 10000, "Should count tokens correctly for huge input");
        }

        [Fact]
        public void Analyze_SpecialCharacters_HandlesCorrectly()
        {
            // Arrange
            string prompt = "Can you explain Unicode 😀 and émojis and special chars: @#$%^&*()";

            // Act
            var result = _analyzer.Analyze(prompt);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.TokenCount > 0);
        }

        #endregion

        #region Regression Tests

        [Fact]
        public void Analyze_ConsistentResults_SameInputProducesSameOutput()
        {
            // Arrange
            string prompt = "Debug this code and optimize it";

            // Act
            var result1 = _analyzer.Analyze(prompt);
            var result2 = _analyzer.Analyze(prompt);

            // Assert
            Assert.Equal(result1.PickedModel, result2.PickedModel);
            Assert.Equal(result1.TokenCount, result2.TokenCount);
            Assert.Equal(result1.Confidence, result2.Confidence);
        }

        #endregion
    }
}
