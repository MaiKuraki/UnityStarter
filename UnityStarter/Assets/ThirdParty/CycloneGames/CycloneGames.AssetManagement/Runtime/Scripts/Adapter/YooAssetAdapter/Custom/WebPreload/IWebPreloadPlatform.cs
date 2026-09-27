#if CYCLONEGAMES_HAS_YOOASSET
using UnityEngine.Networking;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Product-supplied bridge to a mini-game platform cache. Implement this to let YooAsset's Web file system
   /// pre-download bundles into the platform-managed cache before a load requests them.
    /// </summary>
    /// <remarks>
    /// Both members are invoked on the Unity main thread from inside YooAsset's Web file system. They run on the
    /// load path, so implementations must stay allocation-free and must not block on IO: return the answer already
    /// available from the platform SDK, or configure an unsent request and let YooAsset drive it.
    /// </remarks>
    public interface IWebPreloadPlatform
    {
        /// <summary>
        /// Reports whether the queried bundle already exists in the platform cache.
        /// </summary>
        /// <param name="query">
        /// Bundle snapshot owned by the calling strategy. Read it synchronously; do not retain it.
        /// </param>
        bool IsBundleCached(WebPreloadBundleQuery query);

        /// <summary>
        /// Creates a configured but unsent request used to pre-download a bundle into the platform cache.
        /// </summary>
        /// <param name="url">Resolved download URL selected by YooAsset.</param>
        /// <returns>A configured request instance, or null when the platform cannot pre-download this URL.</returns>
        UnityWebRequest CreatePreloadRequest(string url);
    }
}
#endif
