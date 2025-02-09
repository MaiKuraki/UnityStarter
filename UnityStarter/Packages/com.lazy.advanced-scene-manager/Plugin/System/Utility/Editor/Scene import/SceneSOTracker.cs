#if UNITY_EDITOR

using System.Linq;
using UnityEditor;

namespace AdvancedSceneManager.Editor.Utility
{

    class SceneSOTracker : AssetPostprocessor
    {

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
        {
            if (importedAssets.Concat(deletedAssets).Concat(movedFromAssetPaths).Any(SceneImportUtility.StringExtensions.IsScene))
                SceneImportUtility.Notify();
        }
    }

}
#endif
