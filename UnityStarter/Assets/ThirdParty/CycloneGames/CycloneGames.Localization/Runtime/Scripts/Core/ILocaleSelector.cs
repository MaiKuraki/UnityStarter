namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Determines a preferred locale from a specific source.
    /// Implementations must be pure lookups — no side effects, no allocation on miss.
    /// <para>
    /// The <see cref="LocalizationService"/> evaluates selectors in priority order.
    /// The first selector that returns a non-null, available locale wins.
    /// </para>
    /// </summary>
    public interface ILocaleSelector
    {
        /// <summary>
        /// Attempts to select a locale code from this source.
        /// Returns <c>null</c> if this source has no preference.
        /// </summary>
        string GetPreferredLocaleCode();
    }
}
