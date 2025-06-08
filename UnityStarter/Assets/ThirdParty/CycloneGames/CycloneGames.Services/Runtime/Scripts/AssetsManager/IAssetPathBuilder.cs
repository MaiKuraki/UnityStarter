namespace CycloneGames.Service
{
    /// <summary> 
    /// Defines an interface for building asset paths. 
    /// </summary> 
    public interface IAssetPathBuilder
    {
        /// <summary> 
        /// Gets the asset path based on the given key. 
        /// </summary> 
        /// <param name="key">The key used to build the asset path.</param> 
        /// <returns>The full asset path.</returns> 
        string GetAssetPath(string key);
    }
}
