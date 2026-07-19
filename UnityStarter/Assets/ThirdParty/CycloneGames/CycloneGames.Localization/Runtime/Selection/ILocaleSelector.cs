namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Determines a preferred locale from a specific source.
    /// Implementations must be side-effect-free lookups. Selection runs only during initialization.
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
