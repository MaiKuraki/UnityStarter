namespace CycloneGames.Service.Runtime
{
    /// <summary>
    /// Defines a contract for a type that can provide a default instance of another type T.
    /// </summary>
    /// <typeparam name="T">The type of object to provide.</typeparam>
    public interface IDefaultProvider<T> where T : struct
    {
        /// <summary>
        /// Gets a default instance of the structure T.
        /// </summary>
        /// <returns>The default settings object.</returns>
        T GetDefault();
    }
}
