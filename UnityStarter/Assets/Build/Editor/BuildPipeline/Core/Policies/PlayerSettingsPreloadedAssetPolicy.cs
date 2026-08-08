using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal static class PlayerSettingsPreloadedAssetPolicy
    {
        private const int MaximumPreloadedAssetCount = 4096;
        private const int MaximumIdentifierCharacters = 512;

        internal static string[] Capture()
        {
            UnityEngine.Object[] assets = PlayerSettings.GetPreloadedAssets()
                ?? Array.Empty<UnityEngine.Object>();
            if (assets.Length > MaximumPreloadedAssetCount)
            {
                throw new InvalidOperationException(
                    $"PlayerSettings contains more than {MaximumPreloadedAssetCount} preloaded assets.");
            }

            var identifiers = new string[assets.Length];
            for (int index = 0; index < assets.Length; index++)
            {
                UnityEngine.Object asset = assets[index];
                if (asset == null || !EditorUtility.IsPersistent(asset))
                {
                    throw new InvalidOperationException(
                        $"PlayerSettings preloaded asset at index {index} is null or non-persistent.");
                }

                string identifier = GlobalObjectId.GetGlobalObjectIdSlow(asset).ToString();
                if (string.IsNullOrWhiteSpace(identifier)
                    || identifier.Length > MaximumIdentifierCharacters
                    || !GlobalObjectId.TryParse(identifier, out GlobalObjectId parsed)
                    || GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) == null)
                {
                    throw new InvalidOperationException(
                        $"PlayerSettings preloaded asset at index {index} has no stable GlobalObjectId.");
                }

                identifiers[index] = identifier;
            }

            return identifiers;
        }

        internal static void ApplyExact(string[] identifiers)
        {
            ValidateIdentifiers(identifiers, "preloaded asset state");
            var assets = new UnityEngine.Object[identifiers.Length];
            for (int index = 0; index < identifiers.Length; index++)
            {
                if (!GlobalObjectId.TryParse(
                        identifiers[index],
                        out GlobalObjectId identifier))
                {
                    throw new InvalidOperationException(
                        $"PlayerSettings preloaded asset identifier at index {index} is invalid.");
                }

                assets[index] = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(identifier);
                if (assets[index] == null)
                {
                    throw new InvalidOperationException(
                        $"PlayerSettings preloaded asset identifier at index {index} no longer resolves: '{identifiers[index]}'.");
                }
            }

            PlayerSettings.SetPreloadedAssets(assets);
            if (!SequenceEqual(Capture(), identifiers))
            {
                throw new InvalidOperationException(
                    "Unity rejected the exact PlayerSettings preloaded asset sequence.");
            }
        }

        internal static void ValidateIdentifiers(string[] identifiers, string label)
        {
            if (identifiers == null || identifiers.Length > MaximumPreloadedAssetCount)
            {
                throw new InvalidOperationException(
                    $"PlayerSettings {label} is missing or exceeds {MaximumPreloadedAssetCount} entries.");
            }

            for (int index = 0; index < identifiers.Length; index++)
            {
                string identifier = identifiers[index];
                if (string.IsNullOrWhiteSpace(identifier)
                    || identifier.Length > MaximumIdentifierCharacters
                    || !GlobalObjectId.TryParse(identifier, out _))
                {
                    throw new InvalidOperationException(
                        $"PlayerSettings {label} contains an invalid identifier at index {index}.");
                }
            }
        }

        internal static bool SequenceEqual(string[] first, string[] second)
        {
            return first != null
                && second != null
                && first.SequenceEqual(second, StringComparer.Ordinal);
        }
    }
}
