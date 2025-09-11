using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayTags.Editor
{
   public class BuildTags : IPreprocessBuildWithReport
   {
      public int callbackOrder => 0;

      public void OnPreprocessBuild(BuildReport report)
      {
         string streamingAssetsPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "StreamingAssets");
         if (!Directory.Exists(streamingAssetsPath))
         {
            Directory.CreateDirectory(streamingAssetsPath);
         }

         GameplayTagManager.ReloadTags();

         string filePath = Path.Combine(streamingAssetsPath, "GameplayTags");

         using (FileStream file = File.Create(filePath))
         {
            using (BinaryWriter writer = new(file))
            {
               foreach (GameplayTag tag in GameplayTagManager.GetAllTags())
               {
                  if (!tag.IsLeaf)
                     continue;

                  writer.Write(tag.Name);
               }
            }
         }
      }
   }
}
