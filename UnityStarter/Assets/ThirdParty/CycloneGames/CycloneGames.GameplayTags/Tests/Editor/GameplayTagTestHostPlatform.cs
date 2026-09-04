using System;
using System.Collections.Generic;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayTags.Tests
{
   /// <summary>
   /// A host platform the editor test suite can drive directly.
   /// </summary>
   /// <remarks>
   /// Test support only. It installs itself through <see cref="GameplayTagHost.Use"/>, so a test that sets
   /// <see cref="IsRuntimePlaying"/> or <see cref="SetBuildData"/> changes exactly the facts the registry
   /// will read - the same contract a real Unity or headless host implements.
   /// </remarks>
   public sealed class GameplayTagTestHostPlatform : GameplayTagHostPlatformBase
   {
      private bool m_IsRuntimePlaying;
      private byte[] m_BuildData;
      private bool m_HasBuildData;
      private string m_SettingsDirectory;

      public override string Name => "Test";

      /// <summary>Installs this platform as the ambient host and returns it.</summary>
      public static GameplayTagTestHostPlatform Install()
      {
         var platform = new GameplayTagTestHostPlatform();
         GameplayTagHost.Use(platform);
         return platform;
      }

      // The base contract is getter-only - a live platform derives the value from its engine, so there
      // is nothing to assign. The test fixture owns the value and sets it through SetRuntimePlaying.
      public override bool IsRuntimePlaying => m_IsRuntimePlaying;

      /// <summary>Sets the value the overridden getter reports.</summary>
      public void SetRuntimePlaying(bool value)
      {
         m_IsRuntimePlaying = value;
      }

      public string SettingsDirectory
      {
         get => m_SettingsDirectory;
         set => m_SettingsDirectory = value;
      }

      /// <summary>Sets the manifest a build-data source will read, or null to publish none.</summary>
      public void SetBuildData(byte[] data)
      {
         m_BuildData = data;
         m_HasBuildData = data != null;
      }

      public override bool TryLoadBuildTagData(out byte[] data)
      {
         data = m_HasBuildData ? m_BuildData : null;
         return m_HasBuildData;
      }

      public override string GetProjectTagSettingsDirectory() => m_SettingsDirectory;

      public new void ClearRegisteredProjectTagSources() => base.ClearRegisteredProjectTagSources();
   }

   /// <summary>
   /// Static facade the tests use to drive <see cref="GameplayTagTestHostPlatform"/> without each fixture
   /// holding its own reference.
   /// </summary>
   public static class TestHost
   {
      private static GameplayTagTestHostPlatform s_Platform;

      public static GameplayTagTestHostPlatform Platform
      {
         get
         {
            s_Platform ??= GameplayTagTestHostPlatform.Install();
            return s_Platform;
         }
      }

      /// <summary>
      /// Installs a fresh platform as the ambient host and caches it for this facade.
      /// </summary>
      /// <remarks>
      /// Use this instead of <see cref="GameplayTagTestHostPlatform.Install"/> from a fixture: it keeps the
      /// facade's cached instance and <see cref="GameplayTagHost.Current"/> pointing at the same object.
      /// <c>[InitializeOnLoad]</c> and the file watcher's static constructor reinstall the editor platform,
      /// which leaves a directly-installed test platform orphaned - the facade would then configure one
      /// instance while the registry reads another.
      /// </remarks>
      public static GameplayTagTestHostPlatform Install()
      {
         GameplayTagTestHostPlatform platform = GameplayTagTestHostPlatform.Install();
         s_Platform = platform;
         return platform;
      }

      public static bool IsRuntimePlaying
      {
         get => Platform.IsRuntimePlaying;
         set => Platform.SetRuntimePlaying(value);
      }

      public static string SettingsDirectory
      {
         get => Platform.SettingsDirectory;
         set => Platform.SettingsDirectory = value;
      }

      public static void SetBuildData(byte[] data) => Platform.SetBuildData(data);

      public static void RegisterProjectTagSource(IGameplayTagSource source)
         => Platform.RegisterProjectTagSource(source);

      public static void ClearRegisteredProjectTagSources()
         => Platform.ClearRegisteredProjectTagSources();
   }
}
