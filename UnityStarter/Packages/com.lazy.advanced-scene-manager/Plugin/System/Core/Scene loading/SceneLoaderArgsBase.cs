using AdvancedSceneManager.Models;

namespace AdvancedSceneManager.Core
{

    /// <summary>Base class for <see cref="SceneLoadArgs"/> and <see cref="SceneUnloadArgs"/>.</summary>
    public abstract class SceneLoaderArgsBase
    {

        public Scene scene { get; internal set; }
        public SceneCollection collection { get; internal set; }
        public SceneOperation operation { get; internal set; }

        internal bool isHandled { get; set; }
        internal bool noSceneWasLoaded { get; set; }
        public bool isError { get; private set; }
        public string errorMessage { get; private set; }
        public bool reportProgress { get; internal set; } = true;

        public void SetError(string message)
        {
            isError = true;
            isHandled = true;
            errorMessage = message;
        }

        /// <summary>Gets if this scene is a loading screen.</summary>
        public bool isLoadingScreen => scene && scene.isLoadingScreen;

        /// <summary>Gets if this scene is a splash screen.</summary>
        public bool isSplashScreen => scene && scene.isSplashScreen;

    }

}
