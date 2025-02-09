using UnityEngine;
using UnityEditor.AssetImporters;
using System.IO;

#if !UNITY_2022_1_OR_NEWER
    using UnityEditor.Experimental.AssetImporters;
#endif

namespace Proxima.Editor
{
    [ScriptedImporter(1, "pfx")]
    internal class PfxImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = ScriptableObject.CreateInstance<PfxAsset>();
            asset.Bytes = File.ReadAllBytes(ctx.assetPath);
            ctx.AddObjectToAsset("main obj", asset);
            ctx.SetMainObject(asset);
        }
    }
}