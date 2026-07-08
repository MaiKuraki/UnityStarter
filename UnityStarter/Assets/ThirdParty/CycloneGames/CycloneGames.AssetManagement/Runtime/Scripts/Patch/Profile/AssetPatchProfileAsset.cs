using System;

using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    [CreateAssetMenu(menuName = "CycloneGames/AssetManagement/Patch Profile", fileName = "AssetPatchProfile")]
    public sealed class AssetPatchProfileAsset : ScriptableObject
    {
        [SerializeField] private string PackageName = "DefaultPackage";
        [SerializeField] private AssetPatchProfileSettings DefaultSettings = new AssetPatchProfileSettings();
        [SerializeField] private AssetPatchPlatformOverride[] PlatformOverrides = Array.Empty<AssetPatchPlatformOverride>();

        public AssetPatchRuntimeProfile BuildRuntimeProfile()
        {
            return BuildRuntimeProfile(AssetPatchPlatformResolver.Current);
        }

        public AssetPatchRuntimeProfile BuildRuntimeProfile(AssetPatchPlatform platform)
        {
            if (!TryBuildRuntimeProfile(platform, out AssetPatchRuntimeProfile profile, out string error))
            {
                throw new InvalidOperationException(error);
            }

            return profile;
        }

        public bool TryBuildRuntimeProfile(out AssetPatchRuntimeProfile profile, out string error)
        {
            return TryBuildRuntimeProfile(AssetPatchPlatformResolver.Current, out profile, out error);
        }

        public bool TryBuildRuntimeProfile(AssetPatchPlatform platform, out AssetPatchRuntimeProfile profile, out string error)
        {
            profile = default;
            error = null;

            try
            {
                AssetPatchProfileSettings settings = ResolveSettings(platform);
                if (settings == null)
                {
                    error = "Patch profile settings are missing.";
                    return false;
                }

                profile = new AssetPatchRuntimeProfileBuilder()
                    .WithPackageName(PackageName)
                    .WithPlatform(platform)
                    .WithAutoDownloadOnFoundNewVersion(settings.AutoDownload)
                    .WithAppendTimeTicks(settings.AppendTicks)
                    .WithDownloadPolicy(settings.CreateDownloadPolicy())
                    .WithTrustPolicy(settings.CreateTrustPolicy())
                    .Build();
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                error = ex.Message;
                return false;
            }
        }

        private AssetPatchProfileSettings ResolveSettings(AssetPatchPlatform platform)
        {
            if (PlatformOverrides != null)
            {
                for (int i = 0; i < PlatformOverrides.Length; i++)
                {
                    AssetPatchPlatformOverride platformOverride = PlatformOverrides[i];
                    if (platformOverride != null && platformOverride.IsEnabled && platformOverride.TargetPlatform == platform)
                    {
                        return platformOverride.ProfileSettings;
                    }
                }

                for (int i = 0; i < PlatformOverrides.Length; i++)
                {
                    AssetPatchPlatformOverride platformOverride = PlatformOverrides[i];
                    if (platformOverride != null && platformOverride.IsEnabled && platformOverride.TargetPlatform == AssetPatchPlatform.Any)
                    {
                        return platformOverride.ProfileSettings;
                    }
                }
            }

            return DefaultSettings;
        }
    }
}
