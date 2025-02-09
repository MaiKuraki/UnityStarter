#if UNITY_EDITOR

using System.Linq;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Internal;
using AdvancedSceneManager.Utility;
using UnityEditor;

namespace AdvancedSceneManager.Editor.Utility
{

    partial class SceneImportUtility
    {

        public static class StringExtensions
        {

            /// <summary>Gets whatever the paths points to a scene that has been imported.</summary>
            public static bool IsImported(string path) =>
                 IsScene(path) && GetImportedScene(path.ToLower(), out _);

            /// <summary>Gets whatever this scene is blacklisted.</summary>
            public static bool IsBlacklisted(string path) =>
                BlocklistUtility.IsBlacklisted(path.ToLower()) || IsFallbackScene(path.ToLower());

            /// <summary>Gets whatever this scene is an ASM scene.</summary>
            public static bool IsASMScene(string path) =>
                path?.ToLower()?.StartsWith(SceneManager.package.folder.ToLower()) ?? false;

            /// <summary>Gets whatever this scene is a unity test runner scene.</summary>
            public static bool IsTestScene(string path) =>
                path?.ToLower()?.StartsWith("Assets/inittestscene") ?? false;

            /// <summary>Gets whatever this is a package scene.</summary>
            public static bool IsPackageScene(string path) =>
                path?.ToLower()?.StartsWith("packages/") ?? false;

            /// <summary>Gets if this scene is the default scene.</summary>
            public static bool IsFallbackScene(string path) =>
                path?.EndsWith($"/{FallbackSceneUtility.Name}.unity") ?? false || path.EndsWith("/AdvancedSceneManager.unity");

            /// <summary>Gets whatever the path points to a SceneAsset.</summary>
            public static bool IsScene(string path) =>
                path?.EndsWith(".unity") ?? false;

            /// <summary>Gets whatever this <see cref="SceneAsset"/> has an associated <see cref="Scene"/>.</summary>
            public static bool HasScene(string path) =>
                Assets.scenes.Any(s => s.path.ToLower() == path.ToLower());

            /// <summary>Gets whatever this is a scene, that is available for import.</summary>
            public static bool IsValidSceneToImport(string path) =>
                IsScene(path) && !IsImported(path) && !IsBlacklisted(path) && !IsTestScene(path) && !IsPackageScene(path) && !IsASMScene(path);

            /// <summary>Gets whatever this is a dynamic scene (its in a path managed by a dynamic collection).</summary>
            public static bool IsDynamicScene(string path) =>
                SceneManager.profile && SceneManager.profile.dynamicCollections.Any(c => path.ToLower().Contains(c.path.ToLower()));

        }

    }

}
#endif
