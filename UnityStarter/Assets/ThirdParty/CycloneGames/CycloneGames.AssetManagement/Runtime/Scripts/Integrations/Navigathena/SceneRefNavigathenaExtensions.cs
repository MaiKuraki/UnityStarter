#if NAVIGATHENA_PRESENT
using MackySoft.Navigathena.SceneManagement;
using UnityEngine.SceneManagement;

namespace CycloneGames.AssetManagement.Runtime.Integrations.Navigathena
{
    /// <summary>
    /// Extension methods that bridge <see cref="SceneRef"/> to Navigathena's <see cref="ISceneIdentifier"/>.
    /// <code>
    /// // Usage:
    /// ISceneIdentifier id = mySceneRef.ToSceneIdentifier(package);
    /// await navigator.Push(id);
    /// </code>
    /// </summary>
    public static class SceneRefNavigathenaExtensions
    {
        /// <summary>
        /// Creates an <see cref="AssetManagementSceneIdentifier"/> from a <see cref="SceneRef"/>.
        /// </summary>
        public static ISceneIdentifier ToSceneIdentifier(
            this SceneRef sceneRef,
            IAssetPackage package,
            LoadSceneMode loadSceneMode = LoadSceneMode.Single,
            bool activateOnLoad = true)
        {
            return new AssetManagementSceneIdentifier(package, sceneRef.Location, loadSceneMode, activateOnLoad);
        }
    }
}
#endif
