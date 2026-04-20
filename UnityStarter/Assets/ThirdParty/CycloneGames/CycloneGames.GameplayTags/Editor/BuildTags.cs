using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayTags.Editor
{
   public class BuildTags : IPreprocessBuildWithReport, IPostprocessBuildWithReport
   {
      public int callbackOrder => 0;

      private const string GeneratedAssetPath = "Assets/Resources/GameplayTags.bytes";

      public void OnPreprocessBuild(BuildReport report)
      {
         string resourcesPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Resources");
         if (!Directory.Exists(resourcesPath))
         {
            Directory.CreateDirectory(resourcesPath);
         }

         GameplayTagManager.ReloadTags();

         string filePath = Path.Combine(resourcesPath, "GameplayTags.bytes");

         // Collect leaf tags
         List<string> leafTags = new();
         foreach (GameplayTag tag in GameplayTagManager.GetAllTags())
         {
            if (tag.IsLeaf)
               leafTags.Add(tag.Name);
         }

         using (FileStream file = File.Create(filePath))
         {
            using (BinaryWriter writer = new(file))
            {
               // v1 format: [byte version] [int tagCount] [string × tagCount] [uint crc32]
               writer.Write(BuildTagBinaryFormat.CurrentVersion);
               writer.Write(leafTags.Count);

               long dataStart = file.Position;
               foreach (string tagName in leafTags)
               {
                  writer.Write(tagName);
               }
               long dataEnd = file.Position;

               // Compute CRC32 over the tag data region
               writer.Flush();
               file.Position = dataStart;
               byte[] tagData = new byte[dataEnd - dataStart];
               file.Read(tagData, 0, tagData.Length);
               uint crc = BuildTagBinaryFormat.ComputeCrc32(tagData, 0, tagData.Length);

               file.Position = dataEnd;
               writer.Write(crc);
            }
         }

         AssetDatabase.ImportAsset(GeneratedAssetPath, ImportAssetOptions.ForceSynchronousImport);
      }

      public void OnPostprocessBuild(BuildReport report)
      {
         // Clean up generated build-time asset to avoid polluting version control
         if (File.Exists(GeneratedAssetPath))
         {
            AssetDatabase.DeleteAsset(GeneratedAssetPath);
         }

         string metaPath = GeneratedAssetPath + ".meta";
         if (File.Exists(metaPath))
         {
            File.Delete(metaPath);
         }
      }
   }
}
