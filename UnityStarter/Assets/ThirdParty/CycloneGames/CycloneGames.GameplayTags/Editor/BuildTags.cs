using System.IO;
using UnityEditor;
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
         string resourcesPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Resources");
         if (!Directory.Exists(resourcesPath))
         {
            Directory.CreateDirectory(resourcesPath);
         }

         GameplayTagManager.ReloadTags();

         string filePath = Path.Combine(resourcesPath, "GameplayTags.bytes");

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

         AssetDatabase.ImportAsset("Assets/Resources/GameplayTags.bytes", ImportAssetOptions.ForceSynchronousImport);
      }
   }
}
