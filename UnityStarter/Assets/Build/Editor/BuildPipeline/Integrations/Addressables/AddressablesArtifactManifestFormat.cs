using System;
using System.IO;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [Serializable]
    internal sealed class AddressablesArtifactManifest
    {
        public string documentType;
        public string buildTarget;
        public string contentIdentity;
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
        internal const string DocumentType = "addressables-artifact-manifest";
        internal const string FileName = "AddressablesArtifacts.json";

        internal static string Serialize(
            AddressablesArtifactManifest manifest,
            bool prettyPrint)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            manifest.documentType = DocumentType;
            return JsonUtility.ToJson(manifest, prettyPrint);
        }

        internal static AddressablesArtifactManifest Deserialize(
            string json,
            string sourceDescription)
        {
            AddressablesArtifactManifest manifest;
            try
            {
                BuildJsonDocumentContract.Validate<AddressablesArtifactManifest>(
                    json,
                    DocumentType,
                    sourceDescription);
                manifest = JsonUtility.FromJson<AddressablesArtifactManifest>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"{sourceDescription} is not valid JSON.",
                    exception);
            }

            if (manifest == null
                || !string.Equals(
                    manifest.documentType,
                    DocumentType,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{sourceDescription} does not match the current Addressables artifact contract.");
            }

            return manifest;
        }
    }
}
