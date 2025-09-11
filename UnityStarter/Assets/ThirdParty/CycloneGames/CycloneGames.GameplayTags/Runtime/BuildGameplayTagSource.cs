using System;
using System.IO;
using UnityEngine;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayTags.Runtime
{
   internal class BuildGameplayTagSource : IGameplayTagSource
   {
      public string Name => "Build";

      public void RegisterTags(GameplayTagRegistrationContext context)
      {
         try
         {
            using FileStream file = File.OpenRead(Path.Combine(Application.streamingAssetsPath, "GameplayTags"));
            using BinaryReader reader = new(file);

            while (reader.BaseStream.Position != reader.BaseStream.Length)
            {
               string tagName = reader.ReadString();
               context.RegisterTag(tagName, string.Empty, GameplayTagFlags.None, this);
            }
         }
         catch (Exception e)
         {
            Debug.LogError($"Failed to load gameplay tags from StreamingAssets: {e.Message}");
         }
      }
   }
}
