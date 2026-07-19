#if UNITY_EDITOR
using CycloneGames.Localization.Runtime;
using UnityEditor;
using UnityEditor.Callbacks;

namespace CycloneGames.Localization.Editor
{
    internal static class LocalizationAssetOpenHandler
    {
        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            return TryOpen(EditorUtility.InstanceIDToObject(instanceId));
        }

        internal static bool TryOpen(UnityEngine.Object asset)
        {
            switch (asset)
            {
                case StringTable stringTable when !string.IsNullOrEmpty(stringTable.TableId):
                    MultiLanguageStringTableEditor.OpenForTable(stringTable.TableId);
                    return true;
                case AssetTable assetTable when !string.IsNullOrEmpty(assetTable.TableId):
                    AssetTableEditor.OpenForTable(assetTable.TableId);
                    return true;
                case StringTableMetadata metadata when !string.IsNullOrEmpty(metadata.TableId):
                    if (metadata.TableType == TableType.String)
                        MultiLanguageStringTableEditor.OpenForTable(metadata.TableId);
                    else
                        AssetTableEditor.OpenForTable(metadata.TableId);
                    return true;
                default:
                    return false;
            }
        }
    }
}
#endif
