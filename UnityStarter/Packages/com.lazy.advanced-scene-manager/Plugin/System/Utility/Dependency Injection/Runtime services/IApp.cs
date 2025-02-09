using System;
using System.Collections;
using AdvancedSceneManager.Utility;
using UnityEngine;
using app = AdvancedSceneManager.Core.App;

namespace AdvancedSceneManager.DependencyInjection
{

    /// <inheritdoc cref="app"/>
    public interface IApp : DependencyInjectionUtility.IInjectable
    {
        bool isBuildMode { get; }
        bool isQuitting { get; }
        bool isRestart { get; }
        bool isStartupFinished { get; }

        app.StartupProps startupProps { get; set; }

        event Action afterStartup;
        event Action beforeStartup;

        void CancelQuit();
        void CancelStartup();
        void Exit();
        void Quit(bool fade = true, Color? fadeColor = null, float fadeDuration = 1);
        void RegisterQuitCallback(IEnumerator coroutine);
        void UnregisterQuitCallback(IEnumerator coroutine);
        void Restart(app.StartupProps props = null);
        Async<bool> RestartAsync(app.StartupProps props = null);
    }

}
