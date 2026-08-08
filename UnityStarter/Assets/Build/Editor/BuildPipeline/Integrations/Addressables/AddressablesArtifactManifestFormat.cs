using System;
using System.IO;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [Serializable]
    internal sealed class AddressablesArtifactManifest
    {
        public int formatVersion;
        public string buildTarget;
        public string contentVersion;
        public string incrementality;
        public string unityVersion;
        public string activeProfileId;
        public string activeProfileName;
        public string addressablesPlayerVersion;
        public string remoteCatalogLoadPath;
        public AddressablesArtifactManifestEntry[] files;
    }

    [Serializable]
    internal sealed class AddressablesArtifactManifestEntry
    {
        public string kind;
        public string path;
        public long size;
        public string sha256;
    }

    internal static class AddressablesArtifactManifestFormat
    {
        internal const int CurrentVersion = 1;
        internal const string FileName = "AddressablesArtifacts.json";

        internal static string Serialize(
            AddressablesArtifactManifest manifest,
            bool prettyPrint)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            manifest.formatVersion = CurrentVersion;
            return JsonUtility.ToJson(manifest, prettyPrint);
        }

        internal static AddressablesArtifactManifest Deserialize(
            string json,
            string sourceDescription)
        {
            AddressablesArtifactManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<AddressablesArtifactManifest>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"{sourceDescription} is not valid JSON.",
                    exception);
            }

            if (manifest == null
                || manifest.formatVersion != CurrentVersion)
            {
                throw new InvalidDataException(
                    $"{sourceDescription} uses an unsupported format. "
                    + $"Expected formatVersion {CurrentVersion}.");
            }

            return manifest;
        }
    }
}
