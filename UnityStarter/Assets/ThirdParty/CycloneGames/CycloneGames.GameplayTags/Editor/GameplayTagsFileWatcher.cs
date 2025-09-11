using System.IO;
using UnityEditor;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayTags.Editor
{
   [InitializeOnLoad]
   public static class GameplayTagsFileWatcher
   {
      private static FileSystemWatcher s_FileWatcher;

      static GameplayTagsFileWatcher()
      {
         if (!Directory.Exists(FileGameplayTagSource.DirectoryPath))
            return;

         s_FileWatcher = new FileSystemWatcher(FileGameplayTagSource.DirectoryPath, "*.json");
         s_FileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;
         s_FileWatcher.Changed += OnFileChanged;
         s_FileWatcher.Created += OnFileChanged;
         s_FileWatcher.Renamed += OnFileChanged;
         s_FileWatcher.EnableRaisingEvents = true;
      }

      private static void OnFileChanged(object sender, FileSystemEventArgs e)
      {
         EditorApplication.delayCall += () =>
         {
            GameplayTagManager.ReloadTags();
         };
      }
   }
}
