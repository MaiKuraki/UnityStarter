#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;
using CycloneGames.Foundation2D.Runtime;

namespace CycloneGames.Foundation2D.Editor
{
    internal static class SpriteSequenceRendererEditorUtility
    {
        internal readonly struct ScoreDetail
        {
            public readonly string Label;
            public readonly int Points;
            public readonly string Evidence;

            public ScoreDetail(string label, int points, string evidence)
            {
                Label = label;
                Points = points;
                Evidence = evidence;
            }
        }

        internal readonly struct MaterialCandidate
        {
            public readonly Material Material;
            public readonly int Score;
            public readonly string Path;
            public readonly ScoreDetail[] ScoreDetails;

            public MaterialCandidate(Material material, int score, string path, ScoreDetail[] scoreDetails)
            {
                Material = material;
                Score = score;
                Path = path;
                ScoreDetails = scoreDetails;
            }
        }

        internal readonly struct CompatibilitySummary
        {
            public readonly bool HasController;
            public readonly int FrameCount;
            public readonly int DistinctTextureCount;
            public readonly bool FramesShareTexture;
            public readonly bool HasMaterial;
            public readonly bool ShaderMatches;
            public readonly bool MeetsSharedBatchingPrerequisites;
            public readonly string ExpectedShaderName;
            public readonly string SuggestedActions;
            public readonly bool ShouldSuggestFilterFrames;
            public readonly bool ShouldSuggestCreateCorrectMaterial;
            public readonly bool ShouldSuggestSwitchToFlipbookMode;

            public CompatibilitySummary(bool hasController, int frameCount, int distinctTextureCount, bool framesShareTexture, bool hasMaterial, bool shaderMatches, bool meetsSharedBatchingPrerequisites, string expectedShaderName, string suggestedActions, bool shouldSuggestFilterFrames, bool shouldSuggestCreateCorrectMaterial, bool shouldSuggestSwitchToFlipbookMode)
            {
                HasController = hasController;
                FrameCount = frameCount;
                DistinctTextureCount = distinctTextureCount;
                FramesShareTexture = framesShareTexture;
                HasMaterial = hasMaterial;
                ShaderMatches = shaderMatches;
                MeetsSharedBatchingPrerequisites = meetsSharedBatchingPrerequisites;
                ExpectedShaderName = expectedShaderName;
                SuggestedActions = suggestedActions;
                ShouldSuggestFilterFrames = shouldSuggestFilterFrames;
                ShouldSuggestCreateCorrectMaterial = shouldSuggestCreateCorrectMaterial;
                ShouldSuggestSwitchToFlipbookMode = shouldSuggestSwitchToFlipbookMode;
            }
        }

        private static readonly HashSet<Texture> TextureSetBuffer = new();
        private static readonly List<Sprite> SpriteBuffer = new(256);

        public static bool TryGetController(Component context, out SpriteSequenceController controller)
        {
            controller = context != null ? context.GetComponent<SpriteSequenceController>() : null;
            return controller != null;
        }

        public static bool AllFramesShareTexture(Component context)
        {
            if (!TryGetFramesProperty(context, out _, out SerializedProperty frames))
            {
                return true;
            }

            int count = frames.arraySize;
            if (count <= 1)
            {
                return true;
            }

            if (TryBuildFrameList(frames, out List<Sprite> frameSprites) && TryGetCommonAtlas(frameSprites, out _))
            {
                return true;
            }

            Sprite first = frames.GetArrayElementAtIndex(0).objectReferenceValue as Sprite;
            if (first == null || first.texture == null)
            {
                return false;
            }

            Texture texture = first.texture;
            for (int i = 1; i < count; i++)
            {
                Sprite sprite = frames.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
                if (sprite == null || sprite.texture == null || sprite.texture != texture)
                {
                    return false;
                }
            }

            return true;
        }

        public static int CountDistinctFrameTextures(Component context)
        {
            TextureSetBuffer.Clear();
            if (!TryGetFramesProperty(context, out _, out SerializedProperty frames))
            {
                return 0;
            }

            if (TryBuildFrameList(frames, out List<Sprite> frameSprites) && TryGetCommonAtlas(frameSprites, out _))
            {
                return frameSprites.Count > 0 ? 1 : 0;
            }

            for (int i = 0; i < frames.arraySize; i++)
            {
                Sprite sprite = frames.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
                if (sprite != null && sprite.texture != null)
                {
                    TextureSetBuffer.Add(sprite.texture);
                }
            }

            return TextureSetBuffer.Count;
        }

        public static int GetFrameCount(Component context)
        {
            if (!TryGetFramesProperty(context, out _, out SerializedProperty frames))
            {
                return 0;
            }

            return frames.arraySize;
        }

        public static bool KeepOnlyFirstTextureFrames(Component context)
        {
            if (!TryGetFramesProperty(context, out SerializedObject controllerSo, out SerializedProperty frames))
            {
                EditorUtility.DisplayDialog("Filter Failed", "No SpriteSequenceController with frames was found on this object.", "OK");
                return false;
            }

            int count = frames.arraySize;
            if (count <= 1)
            {
                return false;
            }

            Sprite first = frames.GetArrayElementAtIndex(0).objectReferenceValue as Sprite;
            if (first == null || first.texture == null)
            {
                EditorUtility.DisplayDialog("Filter Failed", "First frame is null or has no texture.", "OK");
                return false;
            }

            Texture targetTexture = first.texture;
            SpriteBuffer.Clear();
            for (int i = 0; i < count; i++)
            {
                Sprite sprite = frames.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
                if (sprite != null && sprite.texture == targetTexture)
                {
                    SpriteBuffer.Add(sprite);
                }
            }

            if (SpriteBuffer.Count == 0)
            {
                EditorUtility.DisplayDialog("Filter Failed", "No frame matches the first frame texture.", "OK");
                return false;
            }

            Undo.RecordObject(controllerSo.targetObject, "Filter Frames By First Texture");
            controllerSo.Update();
            frames.arraySize = SpriteBuffer.Count;
            for (int i = 0; i < SpriteBuffer.Count; i++)
            {
                frames.GetArrayElementAtIndex(i).objectReferenceValue = SpriteBuffer[i];
            }
            controllerSo.ApplyModifiedProperties();
            return true;
        }

        public static Material FindBestMaterial(Component context, string shaderName, string ownerName, Material currentMaterial, out string message)
        {
            message = null;

            if (currentMaterial != null && currentMaterial.shader != null && currentMaterial.shader.name == shaderName)
            {
                return currentMaterial;
            }

            List<MaterialCandidate> candidates = GetMaterialCandidates(context, shaderName, ownerName);
            if (candidates.Count == 0)
            {
                message = $"No material using shader {shaderName} was found.";
                return null;
            }

            if (candidates.Count == 1)
            {
                return candidates[0].Material;
            }

            int bestScore = candidates[0].Score;
            int bestCount = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Score != bestScore)
                {
                    break;
                }

                bestCount++;
            }

            if (bestCount > 1)
            {
                message = $"Found multiple equally suitable materials using shader {shaderName}. Choose one from the candidate list.";
                return null;
            }

            return candidates[0].Material;
        }

        public static List<MaterialCandidate> GetMaterialCandidates(Component context, string shaderName, string ownerName)
        {
            string[] guids = AssetDatabase.FindAssets("t:Material");
            List<MaterialCandidate> candidates = new();
            string referenceDirectory = GetReferenceDirectory(context);
            string firstTextureName = GetFirstFrameTextureName(context);
            string ownerHint = Sanitize(ownerName);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == null || material.shader.name != shaderName)
                {
                    continue;
                }

                (int score, ScoreDetail[] details) = ScoreMaterial(path, referenceDirectory, firstTextureName, ownerHint);
                candidates.Add(new MaterialCandidate(material, score, path, details));
            }

            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
            return candidates;
        }

        public static CompatibilitySummary BuildCompatibilitySummary(Component context, Material material, string expectedShaderName, bool suggestSwitchToFlipbookMode)
        {
            bool hasController = TryGetController(context, out _);
            int frameCount = GetFrameCount(context);
            int distinctTextureCount = CountDistinctFrameTextures(context);
            bool framesShareTexture = frameCount <= 1 || AllFramesShareTexture(context);
            bool hasMaterial = material != null;
            bool shaderMatches = !hasMaterial
                ? false
                : material.shader != null && string.Equals(material.shader.name, expectedShaderName, StringComparison.OrdinalIgnoreCase);
            bool meetsSharedBatchingPrerequisites = hasController && frameCount > 0 && framesShareTexture && hasMaterial && shaderMatches;
            string suggestedActions = BuildSuggestedActions(hasController, frameCount, framesShareTexture, hasMaterial, shaderMatches, expectedShaderName, meetsSharedBatchingPrerequisites);
            bool shouldSuggestFilterFrames = hasController && frameCount > 1 && !framesShareTexture;
            bool shouldSuggestCreateCorrectMaterial = hasController && frameCount > 0 && (!hasMaterial || !shaderMatches);

            return new CompatibilitySummary(
                hasController,
                frameCount,
                distinctTextureCount,
                framesShareTexture,
                hasMaterial,
                shaderMatches,
                meetsSharedBatchingPrerequisites,
                expectedShaderName,
                    suggestedActions,
                    shouldSuggestFilterFrames,
                    shouldSuggestCreateCorrectMaterial,
                    suggestSwitchToFlipbookMode);
        }

        public static string FormatCompatibilitySummary(CompatibilitySummary summary, bool requiresExpectedShader)
        {
            string shaderLine = requiresExpectedShader
                ? $"Shader Match: {(summary.ShaderMatches ? "Yes" : "No")} (expected {summary.ExpectedShaderName})"
                : $"Shader Match: {(summary.HasMaterial ? "Not constrained by this mode" : "No material assigned")}";

            string readinessLine = requiresExpectedShader
                ? $"Shared-Material Ready: {(summary.MeetsSharedBatchingPrerequisites ? "Yes" : "No")}"
                : $"Shared-Material Ready: {(summary.FramesShareTexture ? "Potentially, if you switch to a shared-material mode" : "No, frames span multiple textures")}";

            return
                $"Frame Count: {summary.FrameCount}\n" +
                $"Same Texture/Atlas: {(summary.FramesShareTexture ? "Yes" : "No")} (distinct textures: {summary.DistinctTextureCount})\n" +
                $"Material Assigned: {(summary.HasMaterial ? "Yes" : "No")}\n" +
                shaderLine + "\n" +
                readinessLine + "\n" +
                $"Suggested Action: {summary.SuggestedActions}";
        }

        public static Material CreateMaterialAsset(string ownerName, string shaderName, string dialogTitle, string defaultSuffix, string prompt)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("Shader Not Found", $"Cannot find shader {shaderName}. Ensure the shader asset exists and compiles.", "OK");
                return null;
            }

            string path = EditorUtility.SaveFilePanelInProject(
                dialogTitle,
                ownerName + defaultSuffix,
                "mat",
                prompt);

            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            Material material = new(shader);
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(material);
            return material;
        }

        private static bool TryGetFramesProperty(Component context, out SerializedObject controllerSo, out SerializedProperty frames)
        {
            controllerSo = null;
            frames = null;

            if (!TryGetController(context, out SpriteSequenceController controller))
            {
                return false;
            }

            controllerSo = new SerializedObject(controller);
            frames = controllerSo.FindProperty("frames");
            return frames != null;
        }

        private static bool TryBuildFrameList(SerializedProperty frames, out List<Sprite> sprites)
        {
            sprites = null;
            if (frames == null || frames.arraySize <= 0)
            {
                return false;
            }

            List<Sprite> list = new List<Sprite>(frames.arraySize);
            for (int i = 0; i < frames.arraySize; i++)
            {
                Sprite sprite = frames.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
                if (sprite == null)
                {
                    return false;
                }

                list.Add(sprite);
            }

            sprites = list;
            return true;
        }

        private static bool TryGetCommonAtlas(List<Sprite> sprites, out SpriteAtlas atlas)
        {
            atlas = null;
            if (sprites == null || sprites.Count == 0)
            {
                return false;
            }

            string[] atlasGuids = AssetDatabase.FindAssets("t:SpriteAtlas");
            for (int i = 0; i < atlasGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(atlasGuids[i]);
                SpriteAtlas candidate = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                if (candidate == null)
                {
                    continue;
                }

                bool allInThisAtlas = true;
                for (int s = 0; s < sprites.Count; s++)
                {
                    if (!candidate.CanBindTo(sprites[s]))
                    {
                        allInThisAtlas = false;
                        break;
                    }
                }

                if (allInThisAtlas)
                {
                    atlas = candidate;
                    return true;
                }
            }

            return false;
        }

        private static string GetReferenceDirectory(Component context)
        {
            if (!TryGetFramesProperty(context, out _, out SerializedProperty frames) || frames.arraySize <= 0)
            {
                return null;
            }

            Sprite first = frames.GetArrayElementAtIndex(0).objectReferenceValue as Sprite;
            if (first == null)
            {
                return null;
            }

            string spritePath = AssetDatabase.GetAssetPath(first);
            return string.IsNullOrEmpty(spritePath) ? null : Path.GetDirectoryName(spritePath)?.Replace('\\', '/');
        }

        private static string GetFirstFrameTextureName(Component context)
        {
            if (!TryGetFramesProperty(context, out _, out SerializedProperty frames) || frames.arraySize <= 0)
            {
                return null;
            }

            Sprite first = frames.GetArrayElementAtIndex(0).objectReferenceValue as Sprite;
            if (first == null || first.texture == null)
            {
                return null;
            }

            return Sanitize(first.texture.name);
        }

        private static (int score, ScoreDetail[] details) ScoreMaterial(string materialPath, string referenceDirectory, string firstTextureName, string ownerHint)
        {
            int score = 0;
            string normalizedMaterialPath = materialPath.Replace('\\', '/');
            string materialDir = Path.GetDirectoryName(normalizedMaterialPath)?.Replace('\\', '/');
            string fileName = Sanitize(Path.GetFileNameWithoutExtension(normalizedMaterialPath));
            List<ScoreDetail> details = new();

            if (!string.IsNullOrEmpty(referenceDirectory) && !string.IsNullOrEmpty(materialDir))
            {
                int commonSegments = CountCommonSegments(referenceDirectory, materialDir);
                int proximityScore = commonSegments * 100;
                score += proximityScore;
                if (commonSegments > 0)
                {
                    details.Add(new ScoreDetail("Directory proximity", proximityScore, $"Common path segments: {commonSegments}"));
                }

                if (string.Equals(referenceDirectory, materialDir, StringComparison.OrdinalIgnoreCase))
                {
                    score += 500;
                    details.Add(new ScoreDetail("Same directory as first frame", 500, referenceDirectory));
                }
            }

            if (!string.IsNullOrEmpty(firstTextureName) && fileName.Contains(firstTextureName, StringComparison.OrdinalIgnoreCase))
            {
                score += 75;
                details.Add(new ScoreDetail("Matches first texture name", 75, firstTextureName));
            }

            if (!string.IsNullOrEmpty(ownerHint) && fileName.Contains(ownerHint, StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
                details.Add(new ScoreDetail("Matches owner name", 50, ownerHint));
            }

            if (details.Count == 0)
            {
                details.Add(new ScoreDetail("Fallback", 0, "Shader matches but no strong context hints"));
            }

            return (score, details.ToArray());
        }

        private static string BuildSuggestedActions(bool hasController, int frameCount, bool framesShareTexture, bool hasMaterial, bool shaderMatches, string expectedShaderName, bool meetsSharedBatchingPrerequisites)
        {
            if (!hasController)
            {
                return "Add or bind a SpriteSequenceController before relying on renderer compatibility checks.";
            }

            if (frameCount <= 0)
            {
                return "Assign frame sprites in SpriteSequenceController first.";
            }

            if (!framesShareTexture)
            {
                return "Filter frames to one atlas/texture, or stay on SpriteSwap mode instead of shared-material flipbook mode.";
            }

            if (!hasMaterial)
            {
                return $"Assign or create a material using {expectedShaderName}.";
            }

            if (!shaderMatches)
            {
                return $"Switch to a material using {expectedShaderName}, or change render mode if this shader path is not intended.";
            }

            if (meetsSharedBatchingPrerequisites)
            {
                return "Current setup is compatible. Next step is validating batching and frame behavior in Frame Debugger and benchmark scene.";
            }

            return "Configuration is partially compatible. Re-check material mode and frame texture consistency.";
        }

        private static int CountCommonSegments(string a, string b)
        {
            string[] aParts = a.Split('/');
            string[] bParts = b.Split('/');
            int count = Mathf.Min(aParts.Length, bParts.Length);
            int match = 0;
            for (int i = 0; i < count; i++)
            {
                if (!string.Equals(aParts[i], bParts[i], StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                match++;
            }

            return match;
        }

        private static string Sanitize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Replace(" ", string.Empty);
        }
    }
}
#endif