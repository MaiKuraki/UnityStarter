using System;
using System.IO;

namespace CycloneGames.GameplayTags.Runtime
{
   /// <summary>
   /// Loads gameplay tags from a build-time generated Resources asset.
   /// This source is intended for use in builds where tags are pre-compiled for performance.
   /// </summary>
   internal class BuildGameplayTagSource : IGameplayTagSource
   {
      public string Name => "Build";
      private const string TAG_RESOURCE_NAME = "GameplayTags";

      public void RegisterTags(GameplayTagRegistrationContext context)
      {
         try
         {
            byte[] data = GameplayTagRuntimePlatform.LoadBuildTagData?.Invoke();

            if (data == null || data.Length == 0)
            {
               return;
            }

            using MemoryStream memoryStream = new(data);
            using BinaryReader reader = new(memoryStream);

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
               string tagName = reader.ReadString();
               context.RegisterTag(tagName, string.Empty, GameplayTagFlags.None, this);
            }
         }
         catch (Exception e)
         {
            GameplayTagLogger.LogError($"[BuildGameplayTagSource] Failed to load gameplay tags from build data. Exception: {e}");
         }
      }
   }
}
