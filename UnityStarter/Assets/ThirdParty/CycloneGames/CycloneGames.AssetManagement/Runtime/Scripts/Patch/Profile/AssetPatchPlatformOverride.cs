using System;

using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    [Serializable]
    public sealed class AssetPatchPlatformOverride
    {
        [SerializeField] private bool Enabled = true;
        [SerializeField] private AssetPatchPlatform Platform = AssetPatchPlatform.Any;
        [SerializeField] private AssetPatchProfileSettings Settings = new AssetPatchProfileSettings();

        public bool IsEnabled => Enabled;
        public AssetPatchPlatform TargetPlatform => Platform;
        public AssetPatchProfileSettings ProfileSettings => Settings;

        public bool Matches(AssetPatchPlatform platform)
        {
            return Enabled && (Platform == platform || Platform == AssetPatchPlatform.Any);
        }
    }
}
