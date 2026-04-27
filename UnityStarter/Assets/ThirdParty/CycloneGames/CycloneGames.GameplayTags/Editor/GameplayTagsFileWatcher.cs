using System.IO;
using UnityEditor;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;

namespace CycloneGames.GameplayTags.Unity.Editor
{
   [InitializeOnLoad]
   public static class GameplayTagsFileWatcher
   {
      private static FileSystemWatcher s_FileWatcher;
      private static double s_LastReloadTime;
      private const double DebounceInterval = 0.5; // seconds

      static GameplayTagsFileWatcher()
      {
         if (!Directory.Exists(FileGameplayTagSource.DirectoryPath))
            return;

         s_FileWatcher = new FileSystemWatcher(FileGameplayTagSource.DirectoryPath, "*.json");
         s_FileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;
         s_FileWatcher.Changed += OnFileChanged;
         s_FileWatcher.Created += OnFileChanged;
         s_FileWatcher.Deleted += OnFileChanged;
         s_FileWatcher.Renamed += OnFileChanged;
         s_FileWatcher.EnableRaisingEvents = true;

         AssemblyReloadEvents.beforeAssemblyReload += Dispose;
      }

      private static void Dispose()
      {
         if (s_FileWatcher != null)
         {
            s_FileWatcher.EnableRaisingEvents = false;
            s_FileWatcher.Changed -= OnFileChanged;
            s_FileWatcher.Created -= OnFileChanged;
            s_FileWatcher.Deleted -= OnFileChanged;
            s_FileWatcher.Renamed -= OnFileChanged;
            s_FileWatcher.Dispose();
            s_FileWatcher = null;
         }
      }

      private static void OnFileChanged(object sender, FileSystemEventArgs e)
      {
         EditorApplication.delayCall += () =>
         {
            // Debounce: skip if we reloaded very recently
            double now = EditorApplication.timeSinceStartup;
            if (now - s_LastReloadTime < DebounceInterval)
               return;

            s_LastReloadTime = now;
            GameplayTagManager.ReloadTags();
         };
      }
   }
}
