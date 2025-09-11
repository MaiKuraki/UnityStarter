using UnityEditor;
using UnityEngine;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayTags.Editor
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
         if (change == PlayModeStateChange.EnteredPlayMode)
         {
            if (GameplayTagManager.HasBeenReloaded)
            {
               Debug.LogWarning("A domain reload is required for the Gameplay Tags to function correctly." +
                  " Please disable 'Enter Play Mode Options' or trigger a domain reload.");
            }
         }
      }
   }
}
