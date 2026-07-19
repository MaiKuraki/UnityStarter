using System.Runtime.CompilerServices;

namespace CycloneGames.Localization.Core
{
    /// <summary>
    /// Resolves CLDR cardinal plural categories for common languages.
    /// Pure static utility with no shared mutable state.
    /// <para>
    /// StringTable convention: append the suffix to a base key.
    /// For key "item_count", create entries: "item_count.one", "item_count.other", etc.
    /// The system tries the resolved category first, then falls back to ".other".
    /// </para>
    /// </summary>
    public static class PluralRules
    {
        /// <summary>
        /// Auditable data baseline. This implementation is an integer-cardinal subset and does not
        /// provide decimal operands, ordinal rules, or the complete CLDR locale inventory.
        /// </summary>
        public const string RuleSetVersion = "CLDR-48-integer-cardinal-subset";

        private static readonly string[] s_Suffixes = { ".zero", ".one", ".two", ".few", ".many", ".other" };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetSuffix(PluralCategory category) => s_Suffixes[(int)category];

        /// <summary>
        /// Resolves the plural category for a given locale and integer count.
        /// Checks region-specific rules first, then language-level CLDR rules.
        /// </summary>
        public static PluralCategory Resolve(LocaleId locale, int count)
        {
            if (!locale.IsValid) return PluralCategory.Other;

            string code = locale.Code;

            if (code == "pt-PT") return count == 1 ? PluralCategory.One : PluralCategory.Other;

            string lang = locale.Language.Code;
            return ResolveByLanguage(lang, count);
        }

        private static PluralCategory ResolveByLanguage(string lang, int count)
        {
            uint n = count < 0 ? (uint)(-(long)count) : (uint)count;

            return lang switch
            {
                // No plural distinction.
                "zh" or "ja" or "ko" or "vi" or "th" or "id" or "ms" or "my" or "km" or "lo"
                    => PluralCategory.Other,

                // Audited integer rules where both 0 and 1 select one.
                "hi" or "bn" or "gu" or "kn" or "si" or "hy" or "zu"
                    => n <= 1 ? PluralCategory.One : PluralCategory.Other,

                // Hebrew integer cardinal forms.
                "he" => ResolveHebrew(n),

                // Modulo-one languages.
                "is" or "mk" => ResolveModuloOne(n),

                // French-family languages treat 0 and 1 as singular.
                "fr" or "pt" => n <= 1 ? PluralCategory.One : PluralCategory.Other,

                // East Slavic.
                "ru" or "uk" or "be" or "hr" or "sr" or "bs" => ResolveEastSlavic(n),

                // Polish.
                "pl" => ResolvePolish(n),

                // Czech / Slovak.
                "cs" or "sk" => ResolveCzechSlovak(n),

                // Arabic.
                "ar" => ResolveArabic(n),

                // Romanian.
                "ro" or "mo" => ResolveRomanian(n),

                // Lithuanian.
                "lt" => ResolveLithuanian(n),

                // Latvian.
                "lv" => ResolveLatvian(n),

                // Irish.
                "ga" => ResolveIrish(n),

                // Slovenian.
                "sl" => ResolveSlovenian(n),

                // Welsh.
                "cy" => ResolveWelsh(n),

                // Maltese.
                "mt" => ResolveMaltese(n),

                // Generic fallback for languages without an audited specialized rule.
                _ => n == 1 ? PluralCategory.One : PluralCategory.Other,
            };
        }

        // Resolver methods use pure integer math.

        private static PluralCategory ResolveEastSlavic(uint n)
        {
            uint mod10 = n % 10;
            uint mod100 = n % 100;
            if (mod10 == 1 && mod100 != 11) return PluralCategory.One;
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return PluralCategory.Few;
            return PluralCategory.Many;
        }

        private static PluralCategory ResolvePolish(uint n)
        {
            if (n == 1) return PluralCategory.One;
            uint mod10 = n % 10;
            uint mod100 = n % 100;
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return PluralCategory.Few;
            return PluralCategory.Many;
        }

        private static PluralCategory ResolveCzechSlovak(uint n)
        {
            if (n == 1) return PluralCategory.One;
            if (n >= 2 && n <= 4) return PluralCategory.Few;
            return PluralCategory.Other;
        }

        private static PluralCategory ResolveArabic(uint n)
        {
            if (n == 0) return PluralCategory.Zero;
            if (n == 1) return PluralCategory.One;
            if (n == 2) return PluralCategory.Two;
            uint mod100 = n % 100;
            if (mod100 >= 3 && mod100 <= 10) return PluralCategory.Few;
            if (mod100 >= 11 && mod100 <= 99) return PluralCategory.Many;
            return PluralCategory.Other;
        }

        private static PluralCategory ResolveRomanian(uint n)
        {
            if (n == 1) return PluralCategory.One;
            uint mod100 = n % 100;
            if (n == 0 || (mod100 >= 2 && mod100 <= 19)) return PluralCategory.Few;
            return PluralCategory.Other;
        }

        private static PluralCategory ResolveLithuanian(uint n)
        {
            uint mod10 = n % 10;
            uint mod100 = n % 100;
            bool teens = mod100 >= 11 && mod100 <= 19;
            if (mod10 == 1 && !teens) return PluralCategory.One;
            if (mod10 >= 2 && mod10 <= 9 && !teens) return PluralCategory.Few;
            return PluralCategory.Other;
        }

        private static PluralCategory ResolveLatvian(uint n)
        {
            uint mod10 = n % 10;
            uint mod100 = n % 100;
            if (mod10 == 0 || (mod100 >= 11 && mod100 <= 19)) return PluralCategory.Zero;
            if (mod10 == 1 && mod100 != 11) return PluralCategory.One;
            return PluralCategory.Other;
        }

        private static PluralCategory ResolveHebrew(uint n)
        {
            if (n == 1) return PluralCategory.One;
            if (n == 2) return PluralCategory.Two;
            return PluralCategory.Other;
        }

        private static PluralCategory ResolveModuloOne(uint n)
        {
            return n % 10 == 1 && n % 100 != 11
                ? PluralCategory.One
                : PluralCategory.Other;
        }

        private static PluralCategory ResolveIrish(uint n)
        {
            if (n == 1) return PluralCategory.One;
            if (n == 2) return PluralCategory.Two;
            if (n >= 3 && n <= 6) return PluralCategory.Few;
            if (n >= 7 && n <= 10) return PluralCategory.Many;
            return PluralCategory.Other;
        }

        private static PluralCategory ResolveSlovenian(uint n)
        {
            uint mod100 = n % 100;
            if (mod100 == 1) return PluralCategory.One;
            if (mod100 == 2) return PluralCategory.Two;
            if (mod100 == 3 || mod100 == 4) return PluralCategory.Few;
            return PluralCategory.Other;
        }

        private static PluralCategory ResolveWelsh(uint n)
        {
            return n switch
            {
                0 => PluralCategory.Zero,
                1 => PluralCategory.One,
                2 => PluralCategory.Two,
                3 => PluralCategory.Few,
                6 => PluralCategory.Many,
                _ => PluralCategory.Other
            };
        }

        private static PluralCategory ResolveMaltese(uint n)
        {
            if (n == 1) return PluralCategory.One;
            if (n == 2) return PluralCategory.Two;
            uint mod100 = n % 100;
            if (n == 0 || (mod100 >= 3 && mod100 <= 10)) return PluralCategory.Few;
            if (mod100 >= 11 && mod100 <= 19) return PluralCategory.Many;
            return PluralCategory.Other;
        }
    }
}
