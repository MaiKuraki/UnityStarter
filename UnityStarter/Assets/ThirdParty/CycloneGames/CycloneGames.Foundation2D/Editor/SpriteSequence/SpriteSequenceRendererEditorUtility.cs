#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
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
            public readonly bool FramesMeetFlipbookContract;
            public readonly SpriteFlipbookCompatibilityError CompatibilityError;
            public readonly int ErrorFrameIndex;
            public readonly bool HasMaterial;
            public readonly bool ShaderMatches;
            public readonly bool ShaderSupported;
            public readonly bool MeetsSharedBatchingPrerequisites;
            public readonly string ExpectedShaderName;
            public readonly string SuggestedActions;
            public readonly bool ShouldSuggestFilterFrames;
            public readonly bool ShouldSuggestCreateCorrectMaterial;
            public readonly bool ShouldSuggestSwitchToFlipbookMode;

            public CompatibilitySummary(
                bool hasController,
                int frameCount,
                int distinctTextureCount,
                bool framesShareTexture,
                bool framesMeetFlipbookContract,
                SpriteFlipbookCompatibilityError compatibilityError,
                int errorFrameIndex,
                bool hasMaterial,
                bool shaderMatches,
                bool shaderSupported,
                bool meetsSharedBatchingPrerequisites,
                string expectedShaderName,
                string suggestedActions,
                bool shouldSuggestFilterFrames,
                bool shouldSuggestCreateCorrectMaterial,
                bool shouldSuggestSwitchToFlipbookMode)
            {
                HasController = hasController;
                FrameCount = frameCount;
                DistinctTextureCount = distinctTextureCount;
                FramesShareTexture = framesShareTexture;
                FramesMeetFlipbookContract = framesMeetFlipbookContract;
                CompatibilityError = compatibilityError;
                ErrorFrameIndex = errorFrameIndex;
                HasMaterial = hasMaterial;
                ShaderMatches = shaderMatches;
                ShaderSupported = shaderSupported;
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
        private const int MaxRetainedFrameBufferCapacity = 1024;
        private const int DefaultFrameBufferCapacity = 256;

        public static bool TryGetController(Component context, out SpriteSequenceController controller)
        {
            controller = context != null ? context.GetComponent<SpriteSequenceController>() : null;
            return controller != null;
        }

        private static void AnalyzeFrames(
            Component context,
            out bool hasController,
            out int frameCount,
            out int distinctTextureCount,
            out bool framesShareTexture,
            out bool framesMeetFlipbookContract,
            out SpriteFlipbookCompatibilityError compatibilityError,
            out int errorFrameIndex)
        {
            hasController = TryGetFramesProperty(context, out SerializedObject controllerObject, out SerializedProperty frames);
            frameCount = 0;
            distinctTextureCount = 0;
            framesShareTexture = false;
            framesMeetFlipbookContract = false;
            compatibilityError = SpriteFlipbookCompatibilityError.MissingFrames;
            errorFrameIndex = -1;
            if (!hasController)
            {
                return;
            }

            controllerObject.UpdateIfRequiredOrScript();
            frameCount = frames.arraySize;
            if (frameCount <= 0)
            {
                return;
            }

            TextureSetBuffer.Clear();
            SpriteBuffer.Clear();
            try
            {
                bool hasNullOrMissingTexture = false;
                if (SpriteBuffer.Capacity < frameCount)
                {
                    SpriteBuffer.Capacity = frameCount;
                }

                for (int i = 0; i < frameCount; i++)
                {
                    Sprite sprite = frames.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
                    SpriteBuffer.Add(sprite);
                    if (sprite == null || sprite.texture == null)
                    {
                        hasNullOrMissingTexture = true;
                        continue;
                    }

                    TextureSetBuffer.Add(sprite.texture);
                }

                distinctTextureCount = TextureSetBuffer.Count;
                framesShareTexture = !hasNullOrMissingTexture && distinctTextureCount == 1;
                framesMeetFlipbookContract = SpriteFlipbookCompatibility.TryValidateAndBuild(
                    SpriteBuffer,
                    null,
                    out _,
                    out compatibilityError,
                    out errorFrameIndex);
            }
            finally
            {
                bool trimTextureSet = TextureSetBuffer.Count > MaxRetainedFrameBufferCapacity;
                TextureSetBuffer.Clear();
                if (trimTextureSet)
                {
                    TextureSetBuffer.TrimExcess();
                }

                ReleaseSpriteBuffer();
            }
        }

        public static bool KeepOnlyFirstTextureFrames(Component context)
        {
            if (!TryGetFramesProperty(context, out SerializedObject controllerSo, out SerializedProperty frames))
            {
                EditorUtility.DisplayDialog("Filter Failed", "No SpriteSequenceController with frames was found on this object.", "OK");
                return false;
            }

            controllerSo.UpdateIfRequiredOrScript();
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
            int keepCount = 0;
            for (int i = 0; i < count; i++)
            {
                Sprite sprite = frames.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
                if (sprite != null && sprite.texture == targetTexture)
                {
                    keepCount++;
                }
            }

            int removeCount = count - keepCount;
            if (removeCount <= 0)
            {
                EditorUtility.DisplayDialog("No Frames Removed", "Every frame already uses the first frame texture.", "OK");
                return false;
            }

            if (!EditorUtility.DisplayDialog(
                    "Keep Frames Using First Texture",
                    $"This will remove {removeCount} of {count} frames from the sequence. This operation supports Undo.",
                    "Keep Matching Frames",
                    "Cancel"))
            {
                return false;
            }

            SpriteBuffer.Clear();
            try
            {
                if (SpriteBuffer.Capacity < keepCount)
                {
                    SpriteBuffer.Capacity = keepCount;
                }

                for (int i = 0; i < count; i++)
                {
                    Sprite sprite = frames.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
                    if (sprite != null && sprite.texture == targetTexture)
                    {
                        SpriteBuffer.Add(sprite);
                    }
                }

                Undo.RecordObject(controllerSo.targetObject, "Keep Frames Using First Texture");
                frames.arraySize = SpriteBuffer.Count;
                for (int i = 0; i < SpriteBuffer.Count; i++)
                {
                    frames.GetArrayElementAtIndex(i).objectReferenceValue = SpriteBuffer[i];
                }
                controllerSo.ApplyModifiedProperties();
                return true;
            }
            finally
            {
                ReleaseSpriteBuffer();
            }
        }

        private static void ReleaseSpriteBuffer()
        {
            SpriteBuffer.Clear();
            if (SpriteBuffer.Capacity > MaxRetainedFrameBufferCapacity)
            {
                SpriteBuffer.Capacity = DefaultFrameBufferCapacity;
            }
        }

        public static Material SelectBestMaterial(
            List<MaterialCandidate> candidates,
            Material currentMaterial,
            string shaderName,
            out string message)
        {
            message = null;

            if (currentMaterial != null && currentMaterial.shader != null && currentMaterial.shader.name == shaderName)
            {
                return currentMaterial;
            }

            if (candidates == null || candidates.Count == 0)
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
            AnalyzeFrames(
                context,
                out bool hasController,
                out int frameCount,
                out int distinctTextureCount,
                out bool framesShareTexture,
                out bool framesMeetFlipbookContract,
                out SpriteFlipbookCompatibilityError compatibilityError,
                out int errorFrameIndex);
            bool hasMaterial = material != null;
            bool shaderMatches = hasMaterial && material.shader != null &&
                                 string.Equals(material.shader.name, expectedShaderName, StringComparison.Ordinal);
            bool shaderSupported = hasMaterial && material.shader != null && material.shader.isSupported;
            bool meetsSharedBatchingPrerequisites = hasController && frameCount > 0 &&
                                                     framesMeetFlipbookContract && hasMaterial &&
                                                     shaderMatches && shaderSupported;
            string suggestedActions = BuildSuggestedActions(
                hasController,
                frameCount,
                framesShareTexture,
                framesMeetFlipbookContract,
                compatibilityError,
                hasMaterial,
                shaderMatches,
                shaderSupported,
                expectedShaderName,
                meetsSharedBatchingPrerequisites);
            bool shouldSuggestFilterFrames = hasController && frameCount > 1 && !framesShareTexture;
            bool shouldSuggestCreateCorrectMaterial = hasController && frameCount > 0 &&
                                                      (!hasMaterial || !shaderMatches || !shaderSupported);

            return new CompatibilitySummary(
                hasController,
                frameCount,
                distinctTextureCount,
                framesShareTexture,
                framesMeetFlipbookContract,
                compatibilityError,
                errorFrameIndex,
                hasMaterial,
                shaderMatches,
                shaderSupported,
                meetsSharedBatchingPrerequisites,
                expectedShaderName,
                suggestedActions,
                shouldSuggestFilterFrames,
                shouldSuggestCreateCorrectMaterial,
                suggestSwitchToFlipbookMode && framesMeetFlipbookContract);
        }

        public static string FormatCompatibilitySummary(CompatibilitySummary summary, bool requiresExpectedShader)
        {
            string shaderLine = requiresExpectedShader
                ? $"Shader Match: {(summary.ShaderMatches ? "Yes" : "No")} (expected {summary.ExpectedShaderName})"
                : $"Shader Match: {(summary.HasMaterial ? "Not constrained by this mode" : "No material assigned")}";

            string contractLine = summary.FramesMeetFlipbookContract
                ? "Runtime Flipbook Contract: Compatible"
                : summary.ErrorFrameIndex >= 0
                    ? $"Runtime Flipbook Contract: {SpriteFlipbookCompatibility.GetErrorMessage(summary.CompatibilityError)} (frame {summary.ErrorFrameIndex + 1})"
                    : $"Runtime Flipbook Contract: {SpriteFlipbookCompatibility.GetErrorMessage(summary.CompatibilityError)}";

            string readinessLine = requiresExpectedShader
                ? $"Shared-Material Ready: {(summary.MeetsSharedBatchingPrerequisites ? "Yes" : "No")}"
                : $"Shared-Material Ready: {(summary.FramesMeetFlipbookContract ? "Frame data is compatible; assign the dedicated flipbook material when switching modes" : "No")}";

            return
                $"Frame Count: {summary.FrameCount}\n" +
                $"Same Direct Texture: {(summary.FramesShareTexture ? "Yes" : "No")} (distinct textures: {summary.DistinctTextureCount})\n" +
                contractLine + "\n" +
                $"Material Assigned: {(summary.HasMaterial ? "Yes" : "No")}\n" +
                shaderLine + "\n" +
                $"Shader Supported On Current Editor Platform: {(summary.ShaderSupported ? "Yes" : "No")}\n" +
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

            path = AssetDatabase.GenerateUniqueAssetPath(path);
            Material material = null;
            bool assetCreated = false;
            try
            {
                material = new Material(shader)
                {
                    name = Path.GetFileNameWithoutExtension(path),
                };
                AssetDatabase.CreateAsset(material, path);
                assetCreated = true;
                Undo.RegisterCreatedObjectUndo(material, "Create Renderer Material");
                AssetDatabase.SaveAssetIfDirty(material);
                EditorGUIUtility.PingObject(material);
                return material;
            }
            catch (Exception exception)
            {
                if (assetCreated)
                {
                    AssetDatabase.DeleteAsset(path);
                }
                else if (material != null)
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }

                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Material Creation Failed", "The material asset could not be created. See the Console for details.", "OK");
                return null;
            }
        }

        public static bool EnableRequiredCanvasChannels(Canvas canvas, out string failure)
        {
            if (canvas == null)
            {
                failure = "No Canvas is available for this UI renderer.";
                return false;
            }

            AdditionalCanvasShaderChannels current = canvas.additionalShaderChannels;
            AdditionalCanvasShaderChannels required = FlipbookUVMeshEffect.RequiredCanvasChannels;
            AdditionalCanvasShaderChannels updated = current | required;
            if (updated != current)
            {
                Undo.RecordObject(canvas, "Enable Flipbook Canvas Shader Channels");
                canvas.additionalShaderChannels = updated;
                PrefabUtility.RecordPrefabInstancePropertyModifications(canvas);
                EditorUtility.SetDirty(canvas);
            }

            EditorGUIUtility.PingObject(canvas);
            failure = null;
            return true;
        }

        internal static void EnableFlipbookEffect(FlipbookUVMeshEffect effect)
        {
            if (effect == null || effect.enabled)
            {
                return;
            }

            Undo.RecordObject(effect, "Enable Flipbook UV Mesh Effect");
            effect.enabled = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(effect);
            EditorUtility.SetDirty(effect);
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

        private static string BuildSuggestedActions(
            bool hasController,
            int frameCount,
            bool framesShareTexture,
            bool framesMeetFlipbookContract,
            SpriteFlipbookCompatibilityError compatibilityError,
            bool hasMaterial,
            bool shaderMatches,
            bool shaderSupported,
            string expectedShaderName,
            bool meetsSharedBatchingPrerequisites)
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
                return "Keep only frames using one resolved texture, or remain on SpriteSwap mode.";
            }

            if (!framesMeetFlipbookContract)
            {
                return SpriteFlipbookCompatibility.GetErrorMessage(compatibilityError) + " Use SpriteSwap mode until the source sprites satisfy this contract.";
            }

            if (!hasMaterial)
            {
                return $"Assign or create a material using {expectedShaderName}.";
            }

            if (!shaderMatches)
            {
                return $"Switch to a material using {expectedShaderName}, or change render mode if this shader path is not intended.";
            }

            if (!shaderSupported)
            {
                return "The selected shader is unsupported on the current Editor graphics configuration. Validate the target renderer and platform before using it.";
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
