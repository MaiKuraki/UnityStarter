using System.Linq;
using AdvancedSceneManager.Editor.Utility;

namespace AdvancedSceneManager.Legacy
{

    internal static class LegacyUtility
    {

        public static LegacyAssetRef FindAssets() =>
            AssetDatabaseUtility.FindAssets<LegacyAssetRef>().FirstOrDefault();

    }

}
