using System.Linq;
using Xunit;
using ClaudeModelPicker.Services;

namespace ClaudeModelPicker.Tests
{
    /// <summary>
    /// Model-name matching against real Claude Desktop UIA names, whole-word
    /// keywords, the Opus tier, prompt sanitising and config defaults.
    /// </summary>
    public class ModelNamesTests
    {
        [Theory]
        [InlineData("Model: Sonnet 5.5 Medium", "Sonnet", true)]   // chat tab, effort appended
        [InlineData("Model: Opus 5.5", "Opus", true)]              // code tab
        [InlineData("Model: Opus 5.5", "Haiku", false)]
        [InlineData("Model: Haiku 4.5", "haiku", true)]
        [InlineData("Model: Sonnet 5.5 Medium", "Medium", false)]  // effort word is not a model
        [InlineData("Sonnet 5.5", "Sonnet", false)]                // not the model button
        [InlineData(null, "Sonnet", false)]
        public void ButtonShows(string? name, string family, bool expected) =>
            Assert.Equal(expected, ModelNames.ButtonShows(name, family));

        [Theory]
        [InlineData("Opus 5.5 For complex work and everyday tasks", "Opus", true)]
        [InlineData("Sonnet 5.5 Most efficient for simpler tasks", "Sonnet", true)]
        [InlineData("Haiku 4.5 Fastest for quick answers", "Haiku", true)]
        [InlineData("Haiku 4.5 Default", "Haiku", true)]
        [InlineData("Opus 5.5", "Opus", true)]
        [InlineData("Sonnet 5.5", "Haiku", false)]
        [InlineData("Code", "Opus", false)]                        // code tab nav radio
        [InlineData("Claude, unread activity", "Opus", false)]
        [InlineData("More models", "Opus", false)]
        [InlineData("Opusculum 1.0", "Opus", false)]               // whole word only
        [InlineData("", "Opus", false)]
        public void IsMenuItemFor(string name, string family, bool expected) =>
            Assert.Equal(expected, ModelNames.IsMenuItemFor(name, family));

        [Theory]
        [InlineData("Fable 5.1 Requires usage credits")]
        [InlineData("Fable 5.1 Requires usage credits For your toughest challenges Buy credits")]
        public void CreditGatedItems_AreNeverPicked(string name)
        {
            Assert.True(ModelNames.NeedsCredits(name));
            Assert.False(ModelNames.IsMenuItemFor(name, "Fable"));
        }

        [Theory]
        [InlineData("More models", true)]
        [InlineData("more models ", true)]
        [InlineData("Opus 5.5", false)]
        [InlineData(null, false)]
        public void IsMoreModelsItem(string? name, bool expected) =>
            Assert.Equal(expected, ModelNames.IsMoreModelsItem(name));

        [Fact]
        public void Families_SmallestFirst() =>
            Assert.Equal(new[] { "Haiku", "Sonnet", "Opus" }, ModelNames.Families);
    }

    public class KeywordAndTierTests
    {
        private readonly PromptAnalyzer _analyzer = new PromptAnalyzer(new ConfigManager());

        [Theory]
        [InlineData("refactor this method", new[] { "fact" }, false)]
        [InlineData("please listen to this", new[] { "list" }, false)]
        [InlineData("is that reasonable", new[] { "reason" }, false)]
        [InlineData("make a shortcut", new[] { "short" }, false)]
        [InlineData("list the files", new[] { "list" }, true)]
        [InlineData("two lists please", new[] { "list" }, true)]
        [InlineData("I summarized it", new[] { "summarize" }, true)]
        [InlineData("Summarize THIS", new[] { "summarize" }, true)]
        [InlineData("do an in-depth review", new[] { "in-depth" }, true)]
        [InlineData("run a security review now", new[] { "security review" }, true)]
        [InlineData("", new[] { "list" }, false)]
        public void HasKeywords_WholeWords(string text, string[] keywords, bool expected) =>
            Assert.Equal(expected, PromptAnalyzer.HasKeywords(text, keywords));

        [Fact]
        public void Refactor_IsNotCountedAsHaiku()
        {
            // Before: "refactor" contained "fact" -> both tiers scored -> 50/50 tie went to Haiku.
            var pick = _analyzer.Analyze("refactor this");
            Assert.Equal("Sonnet", pick.PickedModel);
        }

        [Fact]
        public void OpusKeyword_ShortPrompt_PicksOpus()
        {
            var pick = _analyzer.Analyze("Do a thorough security review of the auth flow");
            Assert.Equal("Opus", pick.PickedModel);
        }

        [Fact]
        public void VeryLongPrompt_PicksOpus()
        {
            var prompt = string.Join(" ", Enumerable.Range(1, 1200).Select(i => $"uniqueword{i}"));
            var pick = _analyzer.Analyze(prompt);
            Assert.True(pick.TokenCount > 1500, $"got {pick.TokenCount} tokens");
            Assert.Equal("Opus", pick.PickedModel);
        }

        [Fact]
        public void Tie_GoesToSmallerModel()
        {
            // 150..300 tokens scores no length points, so one Haiku word ("list") and
            // one Sonnet word ("compare") tie at 0.5 each. Ties go to the cheaper model.
            var filler = string.Join(" ", Enumerable.Range(1, 90).Select(i => $"item{i}"));
            var pick = _analyzer.Analyze("list and compare " + filler);
            Assert.InRange(pick.TokenCount, 151, 299);
            Assert.Equal(0.5, pick.Confidence, 3);
            Assert.Equal("Haiku", pick.PickedModel);
        }

        [Fact]
        public void Confidence_StaysInRange_ForAllTiers()
        {
            foreach (var p in new[] { "hi", "debug this", "comprehensive audit", string.Join(" ", Enumerable.Range(1, 2000).Select(i => $"w{i}")) })
            {
                var pick = _analyzer.Analyze(p);
                Assert.InRange(pick.Confidence, 0.0, 1.0);
                Assert.Contains(pick.PickedModel, ModelNames.Families);
            }
        }
    }

    public class SanitizeTests
    {
        [Theory]
        [InlineData("my key is sk-abcdefghijklmnopqrstuvwxyz123", "sk-abcdefghijklmnopqrstuvwxyz123")]
        [InlineData("password: hunter2", "hunter2")]
        [InlineData("token=abc123xyz", "abc123xyz")]
        [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload", "eyJhbGciOiJIUzI1NiJ9")]
        [InlineData("AKIAIOSFODNN7EXAMPLE1234", "AKIAIOSFODNN7EXAMPLE1234")]
        public void Secrets_AreRemoved(string prompt, string secret)
        {
            var clean = PromptAnalyzer.SanitizePrompt(prompt);
            Assert.DoesNotContain(secret, clean);
            Assert.Contains("[REDACTED]", clean);
        }

        [Fact]
        public void PlainText_IsUnchanged() =>
            Assert.Equal("explain how DNS works", PromptAnalyzer.SanitizePrompt("explain how DNS works"));
    }

    public class ConfigDefaultsTests
    {
        [Fact]
        public void MissingConfig_UsesSafeDefaults()
        {
            using var config = new ConfigManager("does-not-exist.json");
            Assert.False(config.FlaUIEnabled);
            Assert.True(config.ClipboardEnabled);
            Assert.Equal(1500, config.AnalysisOpusMinTokens);
            Assert.Empty(config.AnalysisOpusKeywords);
        }

        [Fact]
        public void ShippedConfig_HasAllThreeKeywordLists()
        {
            using var config = new ConfigManager();
            Assert.NotEmpty(config.AnalysisHaikuKeywords);
            Assert.NotEmpty(config.AnalysisSonnetKeywords);
            Assert.NotEmpty(config.AnalysisOpusKeywords);
            Assert.True(config.AnalysisHaikuMaxTokens < config.AnalysisSonnetMinTokens);
            Assert.True(config.AnalysisSonnetMinTokens < config.AnalysisOpusMinTokens);
        }
    }
}
