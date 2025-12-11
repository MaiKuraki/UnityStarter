namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Factory interface for creating Dynamic Atlas instances.
    /// </summary>
    public interface IDynamicAtlasFactory
    {
        /// <summary>
        /// Creates a new Dynamic Atlas service instance.
        /// </summary>
        IDynamicAtlas Create(DynamicAtlasConfig config = null);

        /// <summary>
        /// Gets or creates a shared singleton instance.
        /// </summary>
        IDynamicAtlas GetSharedInstance(DynamicAtlasConfig config = null);
    }
}