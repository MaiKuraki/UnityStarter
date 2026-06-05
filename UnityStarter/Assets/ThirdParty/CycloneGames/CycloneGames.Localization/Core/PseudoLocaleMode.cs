using System;

namespace CycloneGames.Localization.Core
{
    /// <summary>
    /// Pseudo-localization modes for QA testing without real translations.
    /// Multiple modes can be combined via bitwise OR.
    /// </summary>
    [Flags]
    public enum PseudoLocaleMode : byte
    {
        /// <summary>Disabled; passes through the original text.</summary>
        None = 0,

        /// <summary>Replace ASCII letters with accented variants.</summary>
        Accents = 1 << 0,

        /// <summary>Pad text with extra characters to simulate longer translations.</summary>
        Elongate = 1 << 1,

        /// <summary>Wrap text in brackets to detect truncation and hardcoded strings.</summary>
        Brackets = 1 << 2,

        /// <summary>Mirror text for RTL testing by reversing character order.</summary>
        Mirror = 1 << 3,

        /// <summary>Common QA preset: accents, elongation, and brackets.</summary>
        Full = Accents | Elongate | Brackets,
    }
}
