namespace CycloneGames.Localization.Runtime
{
    /// <summary>
    /// Unicode CLDR plural categories.
    /// Each language uses a subset of these categories (all languages use <see cref="Other"/>).
    /// </summary>
    public enum PluralCategory : byte
    {
        Zero  = 0,
        One   = 1,
        Two   = 2,
        Few   = 3,
        Many  = 4,
        Other = 5
    }
}
