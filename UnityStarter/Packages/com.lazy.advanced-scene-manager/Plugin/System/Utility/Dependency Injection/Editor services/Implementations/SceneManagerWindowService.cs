#if UNITY_EDITOR
using System;
using AdvancedSceneManager.DependencyInjection.Editor;

namespace AdvancedSceneManager.DependencyInjection
{

    public static partial class DependencyInjectionUtility
    {


        //This implementation will be replaced by AdvancedSceneManager.Editor.UI.SceneManagerWindow.SceneManagerWindowService, when window is ready to do so.
        sealed class SceneManagerWindowService : ISceneManagerWindow
        {

            private SceneManagerWindowService()
            { }

            public static SceneManagerWindowService instance { get; } = new();

            public void CloseWindow() => Throw();
            public void OpenWindow() => Throw();
            public void Reload() => Throw();

            void Throw() =>
                throw new InvalidOperationException("Scene manager window is not available. You are either outside of editor, or window is not yet initialized.");

        }


    }

}

#endif