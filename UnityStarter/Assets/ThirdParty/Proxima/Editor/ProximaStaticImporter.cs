using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEditor.AssetImporters;

#if !UNITY_2022_1_OR_NEWER
    using UnityEditor.Experimental.AssetImporters;
#endif

namespace Proxima.Editor
{
    [ScriptedImporter(1, "proximastatic")]
    internal class ProximaStaticImporter : ScriptedImporter
    {
        // Custom ZIP file importer because ZipFile isn't supported on older Unity versions.
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = ScriptableObject.CreateInstance<ProximaStatic>();
            asset.Files = new List<ProximaStatic.StaticFile>();
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var data = File.ReadAllBytes(ctx.assetPath);
            var br = new BinaryReader(new MemoryStream(data));
            br.ReadByte(); // extra byte

            while (true)
            {
                var sig = br.ReadInt32(); // signature
                if (sig != 0x04034b50)
                {
                    break;
                }

                br.ReadInt16(); // version
                br.ReadInt16(); // flags
                br.ReadInt16(); // compression
                var lastModifiedTime = br.ReadUInt16(); // last modified time
                var lastModifiedDate = br.ReadUInt16(); // last modified date
                br.ReadInt32(); // crc32
                var compressedSize = br.ReadInt32(); // compressed size
                var uncompressedSize = br.ReadInt32(); // uncompressed size
                var nameLength = br.ReadUInt16();
                var extraLength = br.ReadUInt16();
                var name = new string(br.ReadChars(nameLength));
                var extra = br.ReadBytes(extraLength);

                var compressedData = br.ReadBytes(compressedSize);
                var uncompressedData = new byte[uncompressedSize];
                using (var deflateStream = new DeflateStream(new MemoryStream(compressedData), CompressionMode.Decompress))
                {
                    deflateStream.Read(uncompressedData, 0, uncompressedSize);
                }

                var entry = new ProximaStatic.StaticFile();
                entry.Path = name.Replace('\\', '/');
                entry.Bytes = uncompressedData;

                var lastModified = new DateTime(
                    1980 + (lastModifiedDate >> 9),
                    (lastModifiedDate >> 5) & 0xF,
                    lastModifiedDate & 0x1F,
                    lastModifiedTime >> 11,
                    (lastModifiedTime >> 5) & 0x3F,
                    (lastModifiedTime & 0x1F) << 1);

                entry.LastModified = (long)(lastModified - epoch).TotalMilliseconds;

                asset.Files.Add(entry);
            }

            ctx.AddObjectToAsset("main obj", asset);
            ctx.SetMainObject(asset);
        }
    }
}