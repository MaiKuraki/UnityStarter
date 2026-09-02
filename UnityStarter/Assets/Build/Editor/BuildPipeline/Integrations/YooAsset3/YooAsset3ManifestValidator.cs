using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Build.Pipeline.Integrations.YooAsset3.Publication;
using YooAsset;

namespace Build.Pipeline.Editor.Integrations.YooAsset3
{
    /// <summary>
    /// Semantically validates a YooAsset package manifest before it is sealed for
    /// publication. This mirrors the runtime
    /// <c>DeserializeManifestOperation</c> byte layout (little-endian) so a corrupt,
    /// truncated, or version-mismatched manifest fails the build instead of a Player
    /// at load time.
    /// </summary>
    internal static class YooAsset3ManifestValidator
    {
        // YooAsset-3.0.5 PackageManifestConsts.cs
        // These constants are internal in YooAsset, so they are duplicated here.
        private const int MaxFileSize = 104857600;
        private const uint FileMagic = 0x594F4F;
        private const int FileVersion = 2;
        private const int MinFileSize = 41;

        // Collection bounds for the mini-reader. Tag/directory tables and tag/int
        // arrays are length-prefixed by a ushort, so their counts are inherently
        // bounded at 65535; asset and bundle counts are int32 and need an explicit cap.
        private const int MaxAssetCount = 1000000;
        private const int MaxBundleCount = 1000000;

        // EFileNameStyle members (YooAsset-3.0.5 EFileNameStyle.cs:7).
        private const int HashNameStyle = 0;
        private const int BundleNameStyle = 1;
        private const int BundleNameHashStyle = 2;

        public static void ValidatePackageManifest(
            string directory,
            string packageName,
            string packageVersion,
            IManifestDecryptor decryptor,
            bool requireAllBundles,
            List<string> failures)
        {
            // File names follow the default YooAssetConfiguration layout (no
            // PackageFilePrefix): <Package>_<Version>.bytes, .hash, <Package>.version.
            string manifestPath = Path.GetFullPath(Path.Combine(
                directory,
                $"{packageName}_{packageVersion}.bytes"));
            string hashPath = Path.GetFullPath(Path.Combine(
                directory,
                $"{packageName}_{packageVersion}.hash"));
            string versionPath = Path.GetFullPath(Path.Combine(
                directory,
                $"{packageName}.version"));

            if (!PublicationSafety.IsStrictDescendant(directory, manifestPath) ||
                !PublicationSafety.IsStrictDescendant(directory, hashPath) ||
                !PublicationSafety.IsStrictDescendant(directory, versionPath))
            {
                failures.Add($"YooAsset manifest paths escaped their package directory: '{directory}'.");
                return;
            }

            if (!File.Exists(manifestPath))
            {
                failures.Add($"YooAsset package manifest is missing: '{manifestPath}'.");
                return;
            }

            var info = new FileInfo(manifestPath);
            if (info.Length < MinFileSize || info.Length > MaxFileSize)
            {
                failures.Add(
                    $"YooAsset package manifest size is invalid: '{manifestPath}', {info.Length} bytes.");
                return;
            }

            byte[] sourceData;
            try
            {
                sourceData = File.ReadAllBytes(manifestPath);
            }
            catch (Exception exception)
            {
                failures.Add($"YooAsset package manifest could not be read: '{manifestPath}'. {exception.Message}");
                return;
            }

            byte[] decrypted = sourceData;
            if (decryptor != null)
            {
                try
                {
                    byte[] result = decryptor.Decrypt(sourceData);
                    if (result != null)
                    {
                        decrypted = result;
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(
                        $"YooAsset package manifest decryption failed: '{manifestPath}'. {exception.Message}");
                    return;
                }
            }

            int outputNameStyle;
            var bundleFileNames = new List<string>();
            try
            {
                ParseManifest(
                    decrypted,
                    packageName,
                    packageVersion,
                    requireAllBundles,
                    out outputNameStyle,
                    bundleFileNames);
            }
            catch (Exception exception)
            {
                failures.Add($"YooAsset package manifest is malformed: '{manifestPath}'. {exception.Message}");
                return;
            }

            if (requireAllBundles)
            {
                var missing = new List<string>();
                foreach (string bundleFileName in bundleFileNames)
                {
                    string bundlePath = Path.GetFullPath(Path.Combine(directory, bundleFileName));
                    if (!PublicationSafety.IsStrictDescendant(directory, bundlePath))
                    {
                        missing.Add(bundleFileName + " (unsafe path)");
                        continue;
                    }

                    if (!File.Exists(bundlePath))
                    {
                        missing.Add(bundleFileName);
                    }
                }

                if (missing.Count > 0)
                {
                    failures.Add(
                        $"YooAsset package manifest references missing bundle files: {string.Join(", ", missing)}.");
                }
            }

            // The hash is computed over the raw (encrypted) manifest bytes.
            // YooAsset-3.0.5 TaskCreateManifest.cs:94
            string expectedHash = ComputeCrc32Hex(sourceData);
            if (!File.Exists(hashPath))
            {
                failures.Add($"YooAsset package hash file is missing: '{hashPath}'.");
            }
            else
            {
                string actualHash = File.ReadAllText(hashPath).Trim();
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add(
                        $"YooAsset package manifest hash mismatch: expected '{expectedHash}', found '{actualHash}'.");
                }
            }

            if (!File.Exists(versionPath))
            {
                failures.Add($"YooAsset package version file is missing: '{versionPath}'.");
            }
            else
            {
                string actualVersion = File.ReadAllText(versionPath).Trim();
                if (!string.Equals(actualVersion, packageVersion, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"YooAsset package version pointer mismatch: expected '{packageVersion}', found '{actualVersion}'.");
                }
            }
        }

        private static void ParseManifest(
            byte[] data,
            string packageName,
            string packageVersion,
            bool requireAllBundles,
            out int outputNameStyle,
            List<string> bundleFileNames)
        {
            var reader = new ManifestReader(data);

            // File header: magic + version.
            // YooAsset-3.0.5 DeserializeManifestOperation.cs:83-98
            uint magic = reader.ReadUInt32();
            if (magic != FileMagic)
            {
                throw new InvalidOperationException(
                    $"Manifest magic is 0x{magic:X8}, expected 0x{FileMagic:X8}.");
            }

            int version = reader.ReadInt32();
            if (version != FileVersion)
            {
                throw new InvalidOperationException(
                    $"Manifest version is {version}, expected {FileVersion}.");
            }

            bool enableAddressable = reader.ReadBoolean();
            reader.ReadBoolean(); // SupportExtensionless
            reader.ReadBoolean(); // LocationToLower
            bool includeAssetGuid = reader.ReadBoolean();
            reader.ReadBoolean(); // ReplaceAssetPathWithAddress
            outputNameStyle = reader.ReadInt32();
            reader.ReadInt32(); // BuildBundleType
            reader.ReadString(); // BuildPipeline
            string manifestPackageName = reader.ReadString();
            string manifestPackageVersion = reader.ReadString();
            reader.ReadString(); // PackageNote

            if (!string.Equals(manifestPackageName, packageName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Manifest package name '{manifestPackageName}' does not match '{packageName}'.");
            }

            if (!string.Equals(manifestPackageVersion, packageVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Manifest package version '{manifestPackageVersion}' does not match '{packageVersion}'.");
            }

            reader.ReadStringArray(); // tag table
            reader.ReadStringArray(); // directory table

            int assetCount = reader.ReadInt32();
            if (assetCount < 0 || assetCount > MaxAssetCount)
            {
                throw new InvalidOperationException($"Manifest asset count is invalid: {assetCount}.");
            }

            for (int index = 0; index < assetCount; index++)
            {
                if (enableAddressable)
                {
                    reader.ReadString(); // Address
                }

                reader.ReadUInt16(); // directory index
                reader.ReadString(); // file name
                if (includeAssetGuid)
                {
                    reader.ReadHash16(); // AssetGuid
                }

                reader.ReadUInt16Array(); // tags
                reader.ReadInt32(); // BundleID
                reader.ReadInt32Array(); // DependentBundleIDs
            }

            int bundleCount = reader.ReadInt32();
            if (bundleCount < 0 || bundleCount > MaxBundleCount)
            {
                throw new InvalidOperationException($"Manifest bundle count is invalid: {bundleCount}.");
            }

            bundleFileNames.Clear();
            for (int index = 0; index < bundleCount; index++)
            {
                string bundleName = reader.ReadString();
                reader.ReadUInt32(); // UnityCrc
                string fileHash = reader.ReadHash16();
                reader.ReadUInt32(); // FileCrc
                reader.ReadInt64(); // FileSize
                reader.ReadBoolean(); // IsEncrypted
                reader.ReadUInt16Array(); // tags
                reader.ReadInt32Array(); // DependentBundleIDs

                bundleFileNames.Add(GetBundleFileName(outputNameStyle, bundleName, fileHash));
            }
        }

        private static string GetBundleFileName(int nameStyle, string bundleName, string fileHash)
        {
            // YooAsset-3.0.5 BundleFileNaming.cs:17
            switch (nameStyle)
            {
                case HashNameStyle:
                {
                    string fileExtension = Path.GetExtension(bundleName);
                    return fileHash + fileExtension;
                }
                case BundleNameStyle:
                    return bundleName;
                case BundleNameHashStyle:
                {
                    string fileExtension = Path.GetExtension(bundleName);
                    if (string.IsNullOrEmpty(fileExtension))
                    {
                        return bundleName + "_" + fileHash;
                    }

                    string fileName = bundleName.Remove(bundleName.LastIndexOf('.'));
                    return fileName + "_" + fileHash + fileExtension;
                }
                default:
                    throw new InvalidOperationException($"Unsupported bundle file-name style: {nameStyle}.");
            }
        }

        private static string ComputeCrc32Hex(byte[] data)
        {
            // YooAsset-3.0.5 CRC32Algorithm.cs:15
            // Standard CRC-32 (IEEE 802.3): reflected polynomial 0xEDB88320, initial
            // 0xFFFFFFFF, final XOR 0xFFFFFFFF. HashUtility.ToHexString encodes the
            // four little-endian result bytes, so the hex string is byte-reversed
            // relative to the numeric CRC value.
            uint crc = 0xFFFFFFFF;
            foreach (byte value in data)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            }

            crc ^= 0xFFFFFFFF;

            var chars = new char[8];
            for (int index = 0; index < 4; index++)
            {
                byte value = (byte)(crc >> (index * 8));
                chars[index * 2] = ToHexChar(value >> 4);
                chars[index * 2 + 1] = ToHexChar(value & 0x0F);
            }

            return new string(chars);
        }

        private static char ToHexChar(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + value - 10);
        }

        private sealed class ManifestReader
        {
            private readonly byte[] data;
            private int position;

            public ManifestReader(byte[] data)
            {
                this.data = data ?? throw new ArgumentNullException(nameof(data));
            }

            public ushort ReadUInt16()
            {
                Ensure(2);
                ushort value = (ushort)(data[position] | (data[position + 1] << 8));
                position += 2;
                return value;
            }

            public uint ReadUInt32()
            {
                Ensure(4);
                uint value = (uint)(
                    data[position] |
                    (data[position + 1] << 8) |
                    (data[position + 2] << 16) |
                    (data[position + 3] << 24));
                position += 4;
                return value;
            }

            public int ReadInt32()
            {
                return (int)ReadUInt32();
            }

            public long ReadInt64()
            {
                Ensure(8);
                uint low = (uint)(
                    data[position] |
                    (data[position + 1] << 8) |
                    (data[position + 2] << 16) |
                    (data[position + 3] << 24));
                uint high = (uint)(
                    data[position + 4] |
                    (data[position + 5] << 8) |
                    (data[position + 6] << 16) |
                    (data[position + 7] << 24));
                position += 8;
                return (long)low | ((long)high << 32);
            }

            public bool ReadBoolean()
            {
                Ensure(1);
                return data[position++] == 1;
            }

            public string ReadString()
            {
                ushort count = ReadUInt16();
                if (count == 0)
                {
                    return string.Empty;
                }

                Ensure(count);
                string value = Encoding.UTF8.GetString(data, position, count);
                position += count;
                return value;
            }

            public string[] ReadStringArray()
            {
                ushort count = ReadUInt16();
                var values = new string[count];
                for (int index = 0; index < count; index++)
                {
                    values[index] = ReadString();
                }

                return values;
            }

            public ushort[] ReadUInt16Array()
            {
                ushort count = ReadUInt16();
                var values = new ushort[count];
                for (int index = 0; index < count; index++)
                {
                    values[index] = ReadUInt16();
                }

                return values;
            }

            public int[] ReadInt32Array()
            {
                ushort count = ReadUInt16();
                var values = new int[count];
                for (int index = 0; index < count; index++)
                {
                    values[index] = ReadInt32();
                }

                return values;
            }

            public string ReadHash16()
            {
                Ensure(16);
                var chars = new char[32];
                for (int index = 0; index < 16; index++)
                {
                    byte value = data[position++];
                    chars[index * 2] = ToHexChar(value >> 4);
                    chars[index * 2 + 1] = ToHexChar(value & 0x0F);
                }

                return new string(chars);
            }

            private void Ensure(int count)
            {
                if (position + count > data.Length)
                {
                    throw new InvalidOperationException(
                        $"Manifest reader exceeded its buffer at offset {position}.");
                }
            }
        }
    }
}
