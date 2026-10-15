using System;
using UnityEditor;

namespace CycloneGames.UIFramework.Editor.DynamicAtlas
{
    /// <summary>
    /// Turns SpriteAtlas import, delete, and move events into throttled compatibility rescans.
    /// The update pump is registered only while a scan is pending, so the editor hot path stays
    /// event-driven and never polls per frame.
    /// </summary>
    internal sealed class SpriteAtlasImportMonitor : AssetPostprocessor
    {
        private const string SpriteAtlasExtension = ".spriteatlas";

        private static bool _pumpRegistered;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!TouchesSpriteAtlas(importedAssets) &&
                !TouchesSpriteAtlas(deletedAssets) &&
                !TouchesSpriteAtlas(movedAssets) &&
                !TouchesSpriteAtlas(movedFromAssetPaths))
            {
                return;
            }

            if (!SpriteAtlasCompatibilityCache.RequestScan(EditorApplication.timeSinceStartup))
            {
                return;
            }

            RegisterPump();
        }

        private static bool TouchesSpriteAtlas(string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i].EndsWith(SpriteAtlasExtension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RegisterPump()
        {
            if (_pumpRegistered)
            {
                return;
            }

            _pumpRegistered = true;
            EditorApplication.update += Pump;
        }

        private static void UnregisterPump()
        {
            if (!_pumpRegistered)
            {
                return;
            }

            _pumpRegistered = false;
            EditorApplication.update -= Pump;
        }

        private static void Pump()
        {
            if (!SpriteAtlasCompatibilityCache.HasPendingScan)
            {
                UnregisterPump();
                return;
            }

            if (!SpriteAtlasCompatibilityCache.TryConsumePendingScan(EditorApplication.timeSinceStartup))
            {
                return;
            }

            UnregisterPump();
            SpriteAtlasCompatibilityCache.ScanProjectNow();
        }
    }
}
