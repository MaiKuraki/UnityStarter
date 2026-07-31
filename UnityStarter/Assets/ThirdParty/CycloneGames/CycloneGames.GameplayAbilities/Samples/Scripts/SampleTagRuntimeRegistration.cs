using CycloneGames.GameplayTags.Core;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// This class ensures that all project-specific GameplayTags are registered at game startup.
    /// It uses the [RuntimeInitializeOnLoadMethod] attribute to hook into the application's launch process,
    /// guaranteeing that the GameplayTagManager is populated before any gameplay logic runs.
    /// This is the runtime equivalent of the editor's [InitializeOnLoad] script.
    /// </summary>
    public static class SampleTagRuntimeRegistration
    {
        private static readonly LogChannel Log = GameplayAbilitiesSampleLog.Channel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void RegisterTags()
        {
            GameplayTagManager.InitializeIfNeeded();
            Log.Info("Sample GameplayTagManager initialized from build-time tag data.");
        }
    }
}
