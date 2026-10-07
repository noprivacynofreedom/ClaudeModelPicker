using System;
using System.Text.RegularExpressions;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Name rules for Claude Desktop's model picker. Pure string logic, no UI
    /// Automation, so every rule is unit-tested against real UIA names.
    ///
    /// Real names (uia-dump.txt, 2026-09-29 and 2026-10-07):
    ///   Button      "Model: Sonnet 5.5 Medium"   (chat tab, effort appended)
    ///   Button      "Model: Opus 5.5"            (code tab)
    ///   RadioButton "Opus 5.5 For complex work and everyday tasks"
    ///   RadioButton "Haiku 4.5 Default"
    ///   RadioButton "Fable 5.1 Requires usage credits"   (never pick)
    ///   MenuItem    "Fable 5.1 Requires usage credits For your toughest challenges Buy credits"
    /// </summary>
    public static class ModelNames
    {
        public const string ButtonPrefix = "Model:";

        /// <summary>The models the analyzer can recommend, smallest first.</summary>
        public static readonly string[] Families = { "Haiku", "Sonnet", "Opus" };

        public static bool IsModelButton(string? name) =>
            name != null && name.StartsWith(ButtonPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>True when the model button already shows <paramref name="family"/>.</summary>
        public static bool ButtonShows(string? buttonName, string family)
        {
            if (!IsModelButton(buttonName)) return false;
            var shown = buttonName!.Substring(ButtonPrefix.Length).TrimStart();
            return StartsWithWord(shown, family);
        }

        /// <summary>
        /// True when a menu item is the one to invoke for <paramref name="family"/>.
        /// The family must be the first word ("Haiku 4.5 ..."), and items that
        /// need paid credits are never a match.
        /// </summary>
        public static bool IsMenuItemFor(string? itemName, string family)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return false;
            if (NeedsCredits(itemName)) return false;
            return StartsWithWord(itemName.TrimStart(), family);
        }

        /// <summary>The "More models" submenu entry in the code tab's model menu.</summary>
        public static bool IsMoreModelsItem(string? itemName) =>
            itemName != null && itemName.Trim().StartsWith("More models", StringComparison.OrdinalIgnoreCase);

        public static bool NeedsCredits(string? itemName) =>
            itemName != null && itemName.IndexOf("credits", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool StartsWithWord(string text, string word) =>
            !string.IsNullOrEmpty(word) &&
            Regex.IsMatch(text, "^" + Regex.Escape(word) + @"\b", RegexOptions.IgnoreCase);
    }
}
