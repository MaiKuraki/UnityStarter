using System.Runtime.CompilerServices;

namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Resolves CLDR cardinal plural categories for 25+ languages.
    /// Pure static utility — zero allocation, zero state.
    /// <para>
    /// StringTable convention: append the suffix to a base key.
    /// For key "item_count", create entries: "item_count.one", "item_count.other", etc.
    /// The system tries the resolved category first, then falls back to ".other".
    /// </para>
    /// </summary>
    public static class PluralRules
    {
        private static readonly string[] s_Suffixes = { ".zero", ".one", ".two", ".few", ".many", ".other" };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetSuffix(PluralCategory category) => s_Suffixes[(int)category];

        /// <summary>
        /// Resolves the plural category for a given locale and integer count.
        /// Checks region-specific rules first (e.g. pt-PT vs pt-BR), then language-level CLDR rules.
        /// </summary>
        public static PluralCategory Resolve(LocaleId locale, int count)
        {
            if (!locale.IsValid) return PluralCategory.Other;

            string code = locale.Code;

            // Region-specific overrides (rare)
            // pt-PT: strict singular (only n=1). Generic "pt" / pt-BR: n=0,1 both singular.
            if (code == "pt-PT") return count == 1 ? PluralCategory.One : PluralCategory.Other;

            string lang = locale.Language.Code;
            return ResolveByLanguage(lang, count);
        }

        private static PluralCategory ResolveByLanguage(string lang, int n)
        {
            // Absolute value for negative counts (e.g. temperature, score diffs)
            if (n < 0) n = -n;

            return lang switch
            {
                // ── No plural distinction ───────────────────────────────
                // East/Southeast Asian languages
                "zh" or "ja" or "ko" or "vi" or "th" or "id" or "ms" or "my" or "km" or "lo"
                    => PluralCategory.Other,

                // ── Simple singular (n=1) ───────────────────────────────
                // Germanic
                "en" or "de" or "nl" or "sv" or "da" or "no" or "nb" or "nn" or "fy" or "af" or "lb"
                // Romance
                or "it" or "es" or "ca" or "gl" or "eu" or "ast"
                // Others with simple singular
                or "el" or "hu" or "fi" or "et" or "he" or "hi" or "bn" or "ta" or "te" or "mr"
                or "gu" or "kn" or "ml" or "pa" or "si" or "ne" or "ur" or "sw" or "zu"
                or "tr" or "az" or "ka" or "hy" or "bg" or "sq" or "mk" or "is" or "fo"
                    => n == 1 ? PluralCategory.One : PluralCategory.Other,

                // ── French-family (0 and 1 are singular) ────────────────
                "fr" or "pt" => n <= 1 ? PluralCategory.One : PluralCategory.Other,

                // ── East Slavic ─────────────────────────────────────────
                "ru" or "uk" or "be" or "hr" or "sr" or "bs" => ResolveEastSlavic(n),

                // ── Polish ──────────────────────────────────────────────
                "pl" => ResolvePolish(n),

                // ── Czech / Slovak ──────────────────────────────────────
                "cs" or "sk" => ResolveCzechSlovak(n),

                // ── Arabic ──────────────────────────────────────────────
                "ar" => ResolveArabic(n),

                // ── Romanian ────────────────────────────────────────────
                "ro" or "mo" => ResolveRomanian(n),

                // ── Lithuanian ──────────────────────────────────────────
                "lt" => ResolveLithuanian(n),

                // ── Latvian ─────────────────────────────────────────────
                "lv" => ResolveLatvian(n),

                // ── Irish ───────────────────────────────────────────────
                "ga" => ResolveIrish(n),

                // ── Slovenian ───────────────────────────────────────────
                "sl" => ResolveSlovenian(n),

                // ── Welsh ───────────────────────────────────────────────
                "cy" => ResolveWelsh(n),

                // ── Maltese ─────────────────────────────────────────────
                "mt" => ResolveMaltese(n),

                // ── Default: simple singular ────────────────────────────
                _ => n == 1 ? PluralCategory.One : PluralCategory.Other,
            };
        }

        // ── Resolver methods (pure integer math, zero allocation) ───

        // ru, uk, be, hr, sr, bs
        // one:  n%10=1 && n%100≠11
        // few:  n%10=2..4 && n%100≠12..14
        // many: everything else
        private static PluralCategory ResolveEastSlavic(int n)
        {
            int mod10 = n % 10;
            int mod100 = n % 100;
            if (mod10 == 1 && mod100 != 11) return PluralCategory.One;
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return PluralCategory.Few;
            return PluralCategory.Many;
        }

        // pl
        // one:  n=1
        // few:  n%10=2..4 && n%100≠12..14
        // many: everything else (n≠1)
        private static PluralCategory ResolvePolish(int n)
        {
            if (n == 1) return PluralCategory.One;
            int mod10 = n % 10;
            int mod100 = n % 100;
            if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return PluralCategory.Few;
            return PluralCategory.Many;
        }

        // cs, sk
        // one:   n=1
        // few:   n=2..4
        // other: everything else
        private static PluralCategory ResolveCzechSlovak(int n)
        {
            if (n == 1) return PluralCategory.One;
            if (n >= 2 && n <= 4) return PluralCategory.Few;
            return PluralCategory.Other;
        }

        // ar
        // zero:  n=0
        // one:   n=1
        // two:   n=2
        // few:   n%100=3..10
        // many:  n%100=11..99
        // other: everything else (100, 200, etc.)
        private static PluralCategory ResolveArabic(int n)
        {
            if (n == 0) return PluralCategory.Zero;
            if (n == 1) return PluralCategory.One;
            if (n == 2) return PluralCategory.Two;
            int mod100 = n % 100;
            if (mod100 >= 3 && mod100 <= 10) return PluralCategory.Few;
            if (mod100 >= 11 && mod100 <= 99) return PluralCategory.Many;
            return PluralCategory.Other;
        }

        // ro, mo
        // one:   n=1
        // few:   n=0 || n%100=2..19
        // other: everything else
        private static PluralCategory ResolveRomanian(int n)
        {
            if (n == 1) return PluralCategory.One;
            int mod100 = n % 100;
            if (n == 0 || (mod100 >= 2 && mod100 <= 19)) return PluralCategory.Few;
            return PluralCategory.Other;
        }

        // lt
        // one:   n%10=1 && n%100≠11..19
        // few:   n%10=2..9 && n%100≠11..19
        // other: everything else
        private static PluralCategory ResolveLithuanian(int n)
        {
            int mod10 = n % 10;
            int mod100 = n % 100;
            bool teens = mod100 >= 11 && mod100 <= 19;
            if (mod10 == 1 && !teens) return PluralCategory.One;
            if (mod10 >= 2 && mod10 <= 9 && !teens) return PluralCategory.Few;
            return PluralCategory.Other;
        }

        // lv
        // zero:  n=0
        // one:   n%10=1 && n%100≠11
        // other: everything else
        private static PluralCategory ResolveLatvian(int n)
        {
            if (n == 0) return PluralCategory.Zero;
            if (n % 10 == 1 && n % 100 != 11) return PluralCategory.One;
            return PluralCategory.Other;
        }

        // ga (Irish)
        // one:   n=1
        // two:   n=2
        // few:   n=3..6
        // many:  n=7..10
        // other: everything else
        private static PluralCategory ResolveIrish(int n)
        {
            if (n == 1) return PluralCategory.One;
            if (n == 2) return PluralCategory.Two;
            if (n >= 3 && n <= 6) return PluralCategory.Few;
            if (n >= 7 && n <= 10) return PluralCategory.Many;
            return PluralCategory.Other;
        }

        // sl (Slovenian)
        // one:   n%100=1
        // two:   n%100=2
        // few:   n%100=3..4
        // other: everything else
        private static PluralCategory ResolveSlovenian(int n)
        {
            int mod100 = n % 100;
            if (mod100 == 1) return PluralCategory.One;
            if (mod100 == 2) return PluralCategory.Two;
            if (mod100 == 3 || mod100 == 4) return PluralCategory.Few;
            return PluralCategory.Other;
        }

        // cy (Welsh)
        // zero:  n=0
        // one:   n=1
        // two:   n=2
        // few:   n=3
        // many:  n=6
        // other: everything else
        private static PluralCategory ResolveWelsh(int n)
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

        // mt (Maltese)
        // one:   n=1
        // two:   n=2
        // few:   n=0 || n%100=3..10
        // many:  n%100=11..19
        // other: everything else
        private static PluralCategory ResolveMaltese(int n)
        {
            if (n == 1) return PluralCategory.One;
            if (n == 2) return PluralCategory.Two;
            int mod100 = n % 100;
            if (n == 0 || (mod100 >= 3 && mod100 <= 10)) return PluralCategory.Few;
            if (mod100 >= 11 && mod100 <= 19) return PluralCategory.Many;
            return PluralCategory.Other;
        }
    }
}
