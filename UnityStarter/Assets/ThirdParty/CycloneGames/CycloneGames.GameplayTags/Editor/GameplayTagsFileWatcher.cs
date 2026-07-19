using System;
using System.IO;
using System.Threading;
using UnityEditor;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;

namespace CycloneGames.GameplayTags.Unity.Editor
{
   [InitializeOnLoad]
   internal static class GameplayTagsFileWatcher
   {
      private const double DebounceSeconds = 0.5;
      private const double WatcherRetrySeconds = 1.0;
      private const double FaultedWatcherRetrySeconds = 5.0;
      private const double ReloadRetrySeconds = 1.0;
      private const int MaxReloadRetryCount = 3;
      private static FileSystemWatcher s_Watcher;
      private static int s_ReloadRequested;
      private static int s_WatcherFaulted;
      private static long s_LastChangeUtcTicks;
      private static double s_NextWatcherRetryTime;
      private static double s_NextReloadRetryTime;
      private static string s_LastWatcherError;
      private static string s_LastReloadError;
      private static int s_ReloadRetryCount;

      static GameplayTagsFileWatcher()
      {
         GameplayTagManagerEditorInitialization.ConfigureEditorSources();
         EditorApplication.update += OnEditorUpdate;
         EditorApplication.quitting += Dispose;
         AssemblyReloadEvents.beforeAssemblyReload += Dispose;
      }

      private static void OnEditorUpdate()
      {
         if (Interlocked.Exchange(ref s_WatcherFaulted, 0) != 0)
            DisposeWatcher();
         EnsureWatcher();
         if (Volatile.Read(ref s_ReloadRequested) == 0)
            return;
         long elapsedTicks = System.DateTime.UtcNow.Ticks - Interlocked.Read(ref s_LastChangeUtcTicks);
         if (elapsedTicks < DebounceSeconds * System.TimeSpan.TicksPerSecond)
            return;
         if (EditorApplication.timeSinceStartup < s_NextReloadRetryTime)
            return;

         Interlocked.Exchange(ref s_ReloadRequested, 0);
         try
         {
            GameplayTagManager.ReloadTags();
            Interlocked.Exchange(ref s_ReloadRetryCount, 0);
            s_NextReloadRetryTime = 0;
            s_LastReloadError = null;
         }
         catch (Exception exception)
         {
            string message = exception.GetType().Name + ": " + exception.Message;
            bool retry = IsTransientReloadFailure(exception) &&
                         Interlocked.Increment(ref s_ReloadRetryCount) <= MaxReloadRetryCount;
            if (retry)
            {
               s_NextReloadRetryTime = EditorApplication.timeSinceStartup + ReloadRetrySeconds;
               Interlocked.Exchange(ref s_ReloadRequested, 1);
            }
            else
            {
               Interlocked.Exchange(ref s_ReloadRetryCount, 0);
               s_NextReloadRetryTime = 0;
            }

            if (!string.Equals(s_LastReloadError, message, StringComparison.Ordinal))
            {
               s_LastReloadError = message;
               string action = retry
                  ? "A bounded retry was scheduled."
                  : "The current registry snapshot was preserved; another file event is required before retrying.";
               UnityEngine.Debug.LogError($"Gameplay tag catalog reload failed. {action} {message}");
            }
         }
      }

      internal static bool IsTransientReloadFailure(Exception exception)
      {
         for (Exception current = exception; current != null; current = current.InnerException)
         {
            if (current is IOException || current is UnauthorizedAccessException)
               return true;
         }
         return false;
      }

      private static void EnsureWatcher()
      {
         if (s_Watcher != null || EditorApplication.timeSinceStartup < s_NextWatcherRetryTime)
            return;
         s_NextWatcherRetryTime = EditorApplication.timeSinceStartup + WatcherRetrySeconds;

         FileSystemWatcher watcher = null;
         try
         {
            string directory = FileGameplayTagSource.DirectoryPath;
            if (!Directory.Exists(directory))
               return;

            watcher = new FileSystemWatcher(directory, "*.json")
            {
               NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
               IncludeSubdirectories = false,
               EnableRaisingEvents = false
            };
            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Deleted += OnFileChanged;
            watcher.Renamed += OnFileChanged;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;
            s_Watcher = watcher;
            s_LastWatcherError = null;
         }
         catch (Exception exception) when (exception is IOException ||
                                             exception is UnauthorizedAccessException ||
                                             exception is ArgumentException ||
                                             exception is PlatformNotSupportedException)
         {
            DisposeWatcherInstance(watcher);
            s_NextWatcherRetryTime = EditorApplication.timeSinceStartup + FaultedWatcherRetrySeconds;
            string message = exception.GetType().Name + ": " + exception.Message;
            if (!string.Equals(s_LastWatcherError, message, StringComparison.Ordinal))
            {
               s_LastWatcherError = message;
               UnityEngine.Debug.LogWarning($"Gameplay tag file watching is temporarily unavailable. Retrying automatically. {message}");
            }
         }
      }

      private static void OnFileChanged(object sender, FileSystemEventArgs args)
      {
         Interlocked.Exchange(ref s_LastChangeUtcTicks, System.DateTime.UtcNow.Ticks);
         Interlocked.Exchange(ref s_ReloadRetryCount, 0);
         Interlocked.Exchange(ref s_ReloadRequested, 1);
      }

      private static void OnWatcherError(object sender, ErrorEventArgs args)
      {
         Interlocked.Exchange(ref s_WatcherFaulted, 1);
         Interlocked.Exchange(ref s_ReloadRequested, 1);
      }

      private static void Dispose()
      {
         EditorApplication.update -= OnEditorUpdate;
         EditorApplication.quitting -= Dispose;
         AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
         DisposeWatcher();
      }

      private static void DisposeWatcher()
      {
         FileSystemWatcher watcher = s_Watcher;
         s_Watcher = null;
         DisposeWatcherInstance(watcher);
      }

      private static void DisposeWatcherInstance(FileSystemWatcher watcher)
      {
         if (watcher == null)
            return;
         try
         {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnFileChanged;
            watcher.Created -= OnFileChanged;
            watcher.Deleted -= OnFileChanged;
            watcher.Renamed -= OnFileChanged;
            watcher.Error -= OnWatcherError;
            watcher.Dispose();
         }
         catch (Exception exception) when (exception is IOException ||
                                             exception is ObjectDisposedException ||
                                             exception is PlatformNotSupportedException)
         {
            UnityEngine.Debug.LogWarning($"Gameplay tag file watcher cleanup did not complete normally: {exception.Message}");
         }
      }
   }
}
