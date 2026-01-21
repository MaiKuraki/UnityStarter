#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace CycloneGames.UIFramework.Editor.DynamicAtlas
{
    /// <summary>
    /// Professional editor window for validating SpriteAtlas format compatibility 
    /// with CompressedDynamicAtlasService. Optimized for zero-GC performance.
    /// </summary>
    public sealed class SpriteAtlasFormatValidator : EditorWindow
    {
        // === Cached Content (zero-GC) ===
        private static readonly GUIContent s_WindowTitle = new GUIContent("Atlas Format Validator");
        private static readonly GUIContent s_ScanAllContent = new GUIContent("Scan All");
        private static readonly GUIContent s_ScanSelectedContent = new GUIContent("Scan Selected");
        private static readonly GUIContent s_ClearContent = new GUIContent("Clear");
        private static readonly GUIContent s_SelectContent = new GUIContent("Select");
        private static readonly GUIContent s_HelpContent = new GUIContent("?");

        // === Colors ===
        private static readonly Color s_ValidColor = new Color(0.3f, 0.75f, 0.3f);
        private static readonly Color s_InvalidColor = new Color(1.0f, 0.65f, 0.2f);

        // === State ===
        private Vector2 _scrollPos;
        private readonly List<ValidationResult> _results = new List<ValidationResult>(64);
        private BuildTarget _selectedPlatform;
        private bool _showOnlyIssues;
        private int _validCount;
        private int _invalidCount;

        // === Reusable Arrays (zero-GC) ===
        private Sprite[] _spriteBuffer = new Sprite[1];

        private struct ValidationResult
        {
            public string Path;
            public string Name;
            public TextureFormat Format;
            public int BlockSize;
            public bool IsValid;
        }

        [MenuItem("Tools/CycloneGames/Dynamic Atlas/Atlas Format Validator")]
        public static void ShowWindow()
        {
            var window = GetWindow<SpriteAtlasFormatValidator>();
            window.titleContent = s_WindowTitle;
            window.minSize = new Vector2(500, 350);
            window.Show();
        }

        private void OnEnable()
        {
            _selectedPlatform = EditorUserBuildSettings.activeBuildTarget;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);

            // Header
            EditorGUILayout.HelpBox(
                "Validates SpriteAtlas compression formats for CompressedDynamicAtlasService.\n" +
                "Source atlases and dynamic atlas must use the same TextureFormat.",
                MessageType.Info);

            EditorGUILayout.Space(8);

            // Settings Row
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Platform:", GUILayout.Width(60));
            var newPlatform = (BuildTarget)EditorGUILayout.EnumPopup(_selectedPlatform, GUILayout.Width(160));
            if (newPlatform != _selectedPlatform)
            {
                _selectedPlatform = newPlatform;
                if (_results.Count > 0) ScanAllAtlases();
            }
            GUILayout.FlexibleSpace();
            _showOnlyIssues = EditorGUILayout.ToggleLeft("Issues Only", _showOnlyIssues, GUILayout.Width(90));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // Buttons Row
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(s_ScanAllContent, GUILayout.Height(26)))
            {
                ScanAllAtlases();
            }
            if (GUILayout.Button(s_ScanSelectedContent, GUILayout.Height(26)))
            {
                ScanSelectedAtlases();
            }
            GUI.enabled = _results.Count > 0;
            if (GUILayout.Button(s_ClearContent, GUILayout.Height(26), GUILayout.Width(55)))
            {
                _results.Clear();
                _validCount = _invalidCount = 0;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // Summary
            if (_results.Count > 0)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"Total: {_results.Count}", EditorStyles.boldLabel, GUILayout.Width(70));
                var c = GUI.contentColor;
                GUI.contentColor = s_ValidColor;
                EditorGUILayout.LabelField($"✓ {_validCount}", GUILayout.Width(50));
                GUI.contentColor = s_InvalidColor;
                EditorGUILayout.LabelField($"⚠ {_invalidCount}", GUILayout.Width(50));
                GUI.contentColor = c;
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(4);
            }

            // Results List
            DrawResults();
        }

        private void DrawResults()
        {
            if (_results.Count == 0)
            {
                EditorGUILayout.HelpBox("Click 'Scan All' or 'Scan Selected' to validate SpriteAtlas assets.", MessageType.None);
                return;
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            for (int i = 0, count = _results.Count; i < count; i++)
            {
                var r = _results[i];
                if (_showOnlyIssues && r.IsValid) continue;

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                // Name + Format
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(r.Name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(r.Path, EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();

                GUILayout.FlexibleSpace();

                // Status
                EditorGUILayout.BeginVertical(GUILayout.Width(100));
                var c = GUI.contentColor;
                if (r.IsValid)
                {
                    GUI.contentColor = s_ValidColor;
                    EditorGUILayout.LabelField($"✓ {r.Format}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"Block: {r.BlockSize}×{r.BlockSize}", EditorStyles.miniLabel);
                }
                else
                {
                    GUI.contentColor = s_InvalidColor;
                    EditorGUILayout.LabelField($"⚠ {r.Format}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Uncompressed", EditorStyles.miniLabel);
                }
                GUI.contentColor = c;
                EditorGUILayout.EndVertical();

                // Actions
                EditorGUILayout.BeginVertical(GUILayout.Width(60));
                if (GUILayout.Button(s_SelectContent, GUILayout.Height(20)))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(r.Path);
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
                if (!r.IsValid && GUILayout.Button(s_HelpContent, GUILayout.Height(20)))
                {
                    ShowFormatRecommendation();
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void ScanAllAtlases()
        {
            _results.Clear();
            _validCount = _invalidCount = 0;

            var guids = AssetDatabase.FindAssets("t:SpriteAtlas");
            for (int i = 0, len = guids.Length; i < len; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                if (atlas != null) AddResult(atlas, path);
            }
            Repaint();
        }

        private void ScanSelectedAtlases()
        {
            _results.Clear();
            _validCount = _invalidCount = 0;

            var selection = Selection.objects;
            for (int i = 0, len = selection.Length; i < len; i++)
            {
                if (selection[i] is SpriteAtlas atlas)
                {
                    AddResult(atlas, AssetDatabase.GetAssetPath(atlas));
                }
            }

            if (_results.Count == 0)
            {
                EditorUtility.DisplayDialog("Atlas Format Validator",
                    "No SpriteAtlas selected.\nSelect SpriteAtlas assets in Project window first.", "OK");
            }
            Repaint();
        }

        private void AddResult(SpriteAtlas atlas, string path)
        {
            var format = GetAtlasFormat(atlas);
            int blockSize = UIFramework.DynamicAtlas.TextureFormatHelper.GetBlockSize(format);
            bool isValid = blockSize > 1;

            _results.Add(new ValidationResult
            {
                Path = path,
                Name = atlas.name,
                Format = format,
                BlockSize = blockSize,
                IsValid = isValid
            });

            if (isValid) _validCount++; else _invalidCount++;
        }

        private TextureFormat GetAtlasFormat(SpriteAtlas atlas)
        {
            string assetPath = AssetDatabase.GetAssetPath(atlas);
            var importer = AssetImporter.GetAtPath(assetPath) as SpriteAtlasImporter;

            if (importer != null)
            {
                string platformName = GetPlatformName(_selectedPlatform);
                var settings = importer.GetPlatformSettings(platformName);
                if (settings.overridden) return ConvertFormat(settings.format);
                return ConvertFormat(importer.GetPlatformSettings("DefaultTexturePlatform").format);
            }

            int count = atlas.spriteCount;
            if (count > 0)
            {
                if (_spriteBuffer.Length < count) _spriteBuffer = new Sprite[count];
                atlas.GetSprites(_spriteBuffer);
                if (_spriteBuffer[0]?.texture != null) return _spriteBuffer[0].texture.format;
            }
            return TextureFormat.RGBA32;
        }

        private static TextureFormat ConvertFormat(TextureImporterFormat f)
        {
            switch (f)
            {
                case TextureImporterFormat.ASTC_4x4: return TextureFormat.ASTC_4x4;
                case TextureImporterFormat.ASTC_5x5: return TextureFormat.ASTC_5x5;
                case TextureImporterFormat.ASTC_6x6: return TextureFormat.ASTC_6x6;
                case TextureImporterFormat.ASTC_8x8: return TextureFormat.ASTC_8x8;
                case TextureImporterFormat.ASTC_10x10: return TextureFormat.ASTC_10x10;
                case TextureImporterFormat.ASTC_12x12: return TextureFormat.ASTC_12x12;
                case TextureImporterFormat.ETC_RGB4: return TextureFormat.ETC_RGB4;
                case TextureImporterFormat.ETC2_RGB4: return TextureFormat.ETC2_RGB;
                case TextureImporterFormat.ETC2_RGBA8: return TextureFormat.ETC2_RGBA8;
                case TextureImporterFormat.DXT1: return TextureFormat.DXT1;
                case TextureImporterFormat.DXT5: return TextureFormat.DXT5;
                case TextureImporterFormat.BC7: return TextureFormat.BC7;
                case TextureImporterFormat.BC6H: return TextureFormat.BC6H;
                case TextureImporterFormat.BC4: return TextureFormat.BC4;
                case TextureImporterFormat.BC5: return TextureFormat.BC5;
                case TextureImporterFormat.RGBA32: return TextureFormat.RGBA32;
                case TextureImporterFormat.RGB24: return TextureFormat.RGB24;
                case TextureImporterFormat.ARGB32: return TextureFormat.ARGB32;
                case TextureImporterFormat.Alpha8: return TextureFormat.Alpha8;
                default: return TextureFormat.RGBA32;
            }
        }

        private static string GetPlatformName(BuildTarget t)
        {
            switch (t)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneOSX:
                case BuildTarget.StandaloneLinux64: return "Standalone";
                case BuildTarget.iOS: return "iPhone";
                case BuildTarget.Android: return "Android";
                case BuildTarget.WebGL: return "WebGL";
                default: return "Standalone";
            }
        }

        private void ShowFormatRecommendation()
        {
            string rec = _selectedPlatform switch
            {
                BuildTarget.iOS => "Recommended: ASTC 4x4 or ASTC 6x6",
                BuildTarget.Android => "Recommended: ASTC 4x4 or ETC2 RGBA8",
                BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 or
                BuildTarget.StandaloneOSX or BuildTarget.StandaloneLinux64 => "Recommended: BC7 or DXT5",
                BuildTarget.WebGL => "WebGL does not support compressed dynamic atlas.",
                _ => "Select a compressed format."
            };

            EditorUtility.DisplayDialog("Format Recommendation",
                $"Platform: {_selectedPlatform}\n\n{rec}\n\n" +
                "1. Select the SpriteAtlas\n2. Enable platform override\n3. Choose compressed format", "OK");
        }

        [MenuItem("Assets/Validate Atlas Format", true)]
        private static bool ValidateMenuCheck() => Selection.activeObject is SpriteAtlas;

        [MenuItem("Assets/Validate Atlas Format")]
        private static void ValidateFromMenu()
        {
            var w = GetWindow<SpriteAtlasFormatValidator>();
            w.Show();
            w.ScanSelectedAtlases();
        }
    }
}
#endif
