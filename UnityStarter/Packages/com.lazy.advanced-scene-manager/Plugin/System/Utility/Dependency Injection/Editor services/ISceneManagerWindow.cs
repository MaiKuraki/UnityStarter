namespace AdvancedSceneManager.DependencyInjection.Editor
{

#if UNITY_EDITOR

    /// <summary>Provides methods for working with the scene manager window.</summary>
    public interface ISceneManagerWindow : DependencyInjectionUtility.IInjectable
    {

        /// <summary>Close the window.</summary>
        /// <remarks>No effect if already closed.</remarks>
        void CloseWindow();

        /// <summary>Open the window.</summary>
        /// <remarks>No effect if already open.</remarks>
        void OpenWindow();

        /// <summary>Reloads the window.</summary>
        /// <remarks>No effect if not open.</remarks>
        void Reload();

    }

#endif

}
