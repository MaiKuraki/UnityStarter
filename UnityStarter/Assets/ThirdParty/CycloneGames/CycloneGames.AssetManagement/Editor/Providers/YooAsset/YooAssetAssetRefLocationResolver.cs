#if CYCLONEGAMES_HAS_YOOASSET
using System;
using UnityEditor;
using UnityEngine;
using CycloneGames.AssetManagement.Editor;
using YooAsset.Editor;

namespace CycloneGames.AssetManagement.Editor.Providers.YooAsset
{
    /// <summary>
    /// Derives a YooAsset runtime location from the bundle collector configuration.
    /// <para>
    /// An addressable package uses the address produced by the matched collector's <see cref="IAddressRule"/>.
    /// A non-addressable package (or a non-main collector, or the <c>AddressDisable</c> rule) falls back to the
    /// asset path, which is the location YooAsset resolves at runtime in that mode. This mirrors the lookup
    /// contract in <c>YooAssetPackage.TryGetAssetLocationsByTagAsync</c>: address when present, asset path otherwise.
    /// </para>
    /// <para>
    /// Resolution is defensive: a missing address-rule instance or a throwing <see cref="IAddressRule"/> logs a
    /// structured warning and returns null so the registry can fall through to other resolvers. Failures never
    /// propagate to the inspector.
    /// </para>
    /// </summary>
    public sealed class YooAssetAssetRefLocationResolver : IAssetRefLocationResolver
    {
        public int Priority => 0;

        public string ResolveLocation(string assetGuid, string assetPath)
        {
            string activeCollectorPath = null;
            string activeAddressRuleName = null;
            try
            {
                if (string.IsNullOrEmpty(assetPath) || !BundleCollectorSettingData.HasSettingAsset())
                {
                    return null;
                }

                BundleCollectorSetting setting = BundleCollectorSettingData.Setting;
                for (int i = 0; i < setting.Packages.Count; i++)
                {
                    BundleCollectorPackage package = setting.Packages[i];
                    for (int g = 0; g < package.Groups.Count; g++)
                    {
                        BundleCollectorGroup group = package.Groups[g];
                        for (int c = 0; c < group.Collectors.Count; c++)
                        {
                            BundleCollector collector = group.Collectors[c];
                            if (!IsCandidateCollector(collector, assetPath))
                            {
                                continue;
                            }

                            activeCollectorPath = collector.CollectPath;
                            activeAddressRuleName = collector.AddressRuleName;

                            // Addresses only exist for main collectors in an addressable package. Everything else
                            // (AddressDisable, depend/static collectors, non-addressable packages) resolves by asset
                            // path, matching the runtime lookup in YooAssetPackage.
                            if (package.EnableAddressable &&
                                collector.CollectorType == ECollectorType.MainAssetCollector)
                            {
                                IAddressRule rule = BundleCollectorSettingData.GetAddressRuleInstance(
                                    collector.AddressRuleName);
                                if (rule == null)
                                {
                                    Debug.LogWarning(
                                        "[AssetRefLocationResolver] YooAsset address rule '" +
                                        collector.AddressRuleName + "' is missing for collector '" +
                                        collector.CollectPath + "' while resolving asset '" + assetPath +
                                        "'. Returning null so the registry can try other resolvers.");
                                    return null;
                                }

                                string address = rule.GetAssetAddress(new AddressRuleData(
                                    assetPath,
                                    collector.CollectPath,
                                    group.GroupName,
                                    collector.UserData));
                                if (!string.IsNullOrEmpty(address))
                                {
                                    return address;
                                }
                            }

                            return assetPath;
                        }
                    }
                }

                return null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[AssetRefLocationResolver] YooAsset resolver failed for asset '" + assetPath +
                    "', collector '" + activeCollectorPath + "', address rule '" + activeAddressRuleName +
                    "'. Error: " + exception.Message);
                return null;
            }
        }

        private static bool IsCandidateCollector(BundleCollector collector, string assetPath)
        {
            if (string.IsNullOrEmpty(collector.CollectPath))
            {
                return false;
            }

            // Mirrors CollectAssetSearchUtility.IsCandidateCollector but without requiring the asset to exist,
            // so a not-yet-created prefab can be resolved from the collector that would own its folder.
            if (AssetDatabase.IsValidFolder(collector.CollectPath))
            {
                string folderPath = collector.CollectPath.TrimEnd('/') + "/";
                return assetPath.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(assetPath, collector.CollectPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif
