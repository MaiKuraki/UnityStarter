using UnityEditor;
using UnityEngine;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayTags.Unity.Editor
{
   [InitializeOnLoad]
   public static class GameplayTagPlayModeWatcher
   {
      static GameplayTagPlayModeWatcher()
      {
         EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
      }

      private static void OnPlayModeStateChanged(PlayModeStateChange change)
      {
         if (change != PlayModeStateChange.EnteredPlayMode)
         {
            return;
         }

         if (!GameplayTagManager.HasBeenReloaded)
         {
            return;
         }

         if (!EditorSettings.enterPlayModeOptionsEnabled)
         {
            return;
         }

         if ((EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0)
         {
            return;
         }

         Debug.LogWarning("A domain reload is required for the Gameplay Tags to function correctly." +
            " Please disable 'Enter Play Mode Options > Reload Domain' bypass or trigger a domain reload.");
      }
   }
}
