using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    public static class AssetPatchPlatformResolver
    {
        public static AssetPatchPlatform Current => FromRuntimePlatform(Application.platform);

        public static AssetPatchPlatform FromRuntimePlatform(RuntimePlatform platform)
        {
            switch (platform)
            {
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                    return AssetPatchPlatform.Windows;
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXEditor:
                    return AssetPatchPlatform.MacOS;
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:
                    return AssetPatchPlatform.Linux;
                case RuntimePlatform.Android:
                    return AssetPatchPlatform.Android;
                case RuntimePlatform.IPhonePlayer:
                    return AssetPatchPlatform.IOS;
                case RuntimePlatform.WebGLPlayer:
                    return AssetPatchPlatform.WebGL;
                default:
                    return AssetPatchPlatform.Any;
            }
        }
    }
}
