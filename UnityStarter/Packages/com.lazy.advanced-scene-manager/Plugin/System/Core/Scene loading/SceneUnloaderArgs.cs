namespace AdvancedSceneManager.Core
{

    /// <summary>Specifies arguments for <see cref="SceneLoader.UnloadScene(Models.Scene, SceneUnloadArgs)"/>.</summary>
    public class SceneUnloadArgs : SceneLoaderArgsBase
    {

        /// <summary>Notifies ASM that the unload is done.</summary>
        public void SetCompleted() =>
            isHandled = true;

    }

}
