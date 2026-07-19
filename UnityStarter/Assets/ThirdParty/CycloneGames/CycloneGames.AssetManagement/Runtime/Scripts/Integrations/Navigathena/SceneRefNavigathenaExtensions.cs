#if CYCLONEGAMES_HAS_NAVIGATHENA
using MackySoft.Navigathena.SceneManagement;
using UnityEngine.SceneManagement;

namespace CycloneGames.AssetManagement.Runtime.Integrations.Navigathena
{
    /// <summary>
    /// Extension methods that bridge <see cref="SceneRef"/> to Navigathena's <see cref="ISceneIdentifier"/>.
    /// <code>
    /// // Usage:
    /// ISceneIdentifier id = mySceneRef.ToSceneIdentifier(sceneLoader);
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
            IAssetSceneLoader sceneLoader,
            LoadSceneMode loadSceneMode = LoadSceneMode.Additive,
            bool activateOnLoad = true,
            string bucket = null)
        {
            return new AssetManagementSceneIdentifier(
                sceneLoader,
                sceneRef.Location,
                loadSceneMode,
                activateOnLoad,
                bucket);
        }

        public static ISceneIdentifier ToSceneIdentifier(
            this SceneRef sceneRef,
            IAssetSceneLoader sceneLoader,
            LoadSceneParameters loadParameters,
            SceneActivationMode activationMode = SceneActivationMode.ActivateOnLoad,
            string bucket = null)
        {
            return new AssetManagementSceneIdentifier(
                sceneLoader,
                sceneRef.Location,
                loadParameters,
                activationMode,
                bucket);
        }
    }
}
#endif
