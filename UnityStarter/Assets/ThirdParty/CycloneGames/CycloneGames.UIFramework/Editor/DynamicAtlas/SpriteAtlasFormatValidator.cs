using System;
using System.Collections.Generic;
using CycloneGames.UIFramework.DynamicAtlas;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace CycloneGames.UIFramework.Editor.DynamicAtlas
{
    public sealed class SpriteAtlasFormatValidator : EditorWindow
    {
        internal enum Compatibility
        {
            FastPathCandidate = 0,
            FastPathWithCpuFallback = 1,
            ReadbackRequired = 2,
            UnsupportedPacking = 3,
        }

        private readonly struct ValidationResult
        {
            internal readonly string Path;
            internal readonly string Name;
            internal readonly string FormatName;
            internal readonly bool Readable;
            internal readonly bool RotationEnabled;
            internal readonly bool TightPackingEnabled;
            internal readonly Compatibility Compatibility;

            internal ValidationResult(
                string path,
                string name,
                string formatName,
                bool readable,
                bool rotationEnabled,
                bool tightPackingEnabled,
                Compatibility compatibility)
            {
                Path = path;
                Name = name;
                FormatName = formatName;
                Readable = readable;
                RotationEnabled = rotationEnabled;
                TightPackingEnabled = tightPackingEnabled;
                Compatibility = compatibility;
            }
        }

        private static readonly GUIContent ScanAllContent = new GUIContent("Scan All SpriteAtlases", "Runs an explicit project-wide AssetDatabase scan.");
        private static readonly GUIContent ScanSelectedContent = new GUIContent("Scan Selection", "Validates only selected SpriteAtlas assets.");

        private readonly List<ValidationResult> _results = new List<ValidationResult>(64);
        private BuildTarget _selectedPlatform;
        private Vector2 _scrollPosition;
        private bool _issuesOnly;
        private int _fastPathCount;
        private int _cpuFallbackCount;
        private int _readbackCount;
        private int _unsupportedCount;
        private Sprite[] _spriteBuffer = new Sprite[1];

        [MenuItem("Tools/CycloneGames/UI Framework/SpriteAtlas Compatibility Validator")]
        public static void ShowWindow()
        {
            SpriteAtlasFormatValidator window = GetWindow<SpriteAtlasFormatValidator>();
            window.titleContent = new GUIContent("Atlas Validator");
            window.minSize = new Vector2(620f, 400f);
            window.Show();
        }

        private void OnEnable()
        {
            _selectedPlatform = EditorUserBuildSettings.activeBuildTarget;
        }

        private void OnDisable()
        {
            ClearSpriteBuffer();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Dynamic UI atlas pages prefer direct GPU copy. This validator identifies same-format GPU candidates, readable sources that can also use the bounded CPU raw fallback, sources that require explicit synchronous readback, and unsupported packed geometry.",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Target Platform", GUILayout.Width(96f));
            BuildTarget nextPlatform = (BuildTarget)EditorGUILayout.EnumPopup(
                _selectedPlatform,
                GUILayout.Width(190f));
            if (nextPlatform != _selectedPlatform)
            {
                _selectedPlatform = nextPlatform;
                ClearResults();
            }

            GUILayout.FlexibleSpace();
            _issuesOnly = EditorGUILayout.ToggleLeft("Fallback / Unsupported Only", _issuesOnly, GUILayout.Width(184f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(ScanSelectedContent, GUILayout.Height(26f)))
            {
                ScanSelection();
            }

            if (GUILayout.Button(ScanAllContent, GUILayout.Height(26f)))
            {
                ScanAll();
            }

            using (new EditorGUI.DisabledScope(_results.Count == 0))
            {
                if (GUILayout.Button("Clear", GUILayout.Width(58f), GUILayout.Height(26f)))
                {
                    ClearResults();
                }
            }

            EditorGUILayout.EndHorizontal();

            DrawSummary();
            DrawResults();
        }

        private void DrawSummary()
        {
            if (_results.Count == 0)
            {
                return;
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Total: {_results.Count}", EditorStyles.boldLabel, GUILayout.Width(82f));
            EditorGUILayout.LabelField($"GPU only: {_fastPathCount}", GUILayout.Width(104f));
            EditorGUILayout.LabelField($"GPU + CPU: {_cpuFallbackCount}", GUILayout.Width(112f));
            EditorGUILayout.LabelField($"Readback: {_readbackCount}", GUILayout.Width(100f));
            EditorGUILayout.LabelField($"Unsupported: {_unsupportedCount}", GUILayout.Width(112f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawResults()
        {
            if (_results.Count == 0)
            {
                EditorGUILayout.HelpBox("No cached results. A project-wide scan runs only when requested.", MessageType.None);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            for (int i = 0; i < _results.Count; i++)
            {
                ValidationResult result = _results[i];
                if (_issuesOnly && result.Compatibility == Compatibility.FastPathCandidate)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(result.Name, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                DrawCompatibilityLabel(result.Compatibility);
                if (GUILayout.Button("Select", GUILayout.Width(56f)))
                {
                    UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(result.Path);
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(result.Path, EditorStyles.miniLabel);
                EditorGUILayout.LabelField(
                    $"{_selectedPlatform}: {result.FormatName}  |  Readable: {(result.Readable ? "On" : "Off")}  |  Rotation: {(result.RotationEnabled ? "On" : "Off")}  |  Tight Packing: {(result.TightPackingEnabled ? "On" : "Off")}",
                    EditorStyles.miniLabel);

                switch (result.Compatibility)
                {
                    case Compatibility.FastPathCandidate:
                        EditorGUILayout.HelpBox("Direct GPU-copy candidate. Runtime GraphicsFormat equality and target-device CopyTexture support still require Player validation. CPU raw fallback is unavailable because the packed texture is not readable.", MessageType.None);
                        break;
                    case Compatibility.FastPathWithCpuFallback:
                        EditorGUILayout.HelpBox("Direct GPU copy remains the preferred path. If the running device cannot use it, AllowCpuRawCopy may use this readable RGBA32 source with a CPU-backed page and its larger memory budget.", MessageType.Info);
                        break;
                    case Compatibility.ReadbackRequired:
                        EditorGUILayout.HelpBox("The imported source format does not match the RGBA32 page. CPU raw copy cannot convert formats. Provide a matching uncompressed source variant or explicitly permit synchronous readback only on a measured loading path.", MessageType.Warning);
                        break;
                    case Compatibility.UnsupportedPacking:
                        EditorGUILayout.HelpBox("Disable rotation and Tight Packing. Runtime rectangular copying cannot preserve rotated or tight sprite mesh geometry.", MessageType.Error);
                        break;
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawCompatibilityLabel(Compatibility compatibility)
        {
            Color previous = GUI.contentColor;
            switch (compatibility)
            {
                case Compatibility.FastPathCandidate:
                    GUI.contentColor = new Color(0.35f, 0.85f, 0.45f);
                    EditorGUILayout.LabelField("GPU Candidate", EditorStyles.boldLabel, GUILayout.Width(104f));
                    break;
                case Compatibility.FastPathWithCpuFallback:
                    GUI.contentColor = new Color(0.3f, 0.78f, 0.9f);
                    EditorGUILayout.LabelField("GPU + CPU", EditorStyles.boldLabel, GUILayout.Width(88f));
                    break;
                case Compatibility.ReadbackRequired:
                    GUI.contentColor = new Color(1f, 0.72f, 0.24f);
                    EditorGUILayout.LabelField("Readback", EditorStyles.boldLabel, GUILayout.Width(76f));
                    break;
                default:
                    GUI.contentColor = new Color(1f, 0.35f, 0.35f);
                    EditorGUILayout.LabelField("Unsupported", EditorStyles.boldLabel, GUILayout.Width(92f));
                    break;
            }

            GUI.contentColor = previous;
        }

        private void ScanAll()
        {
            ClearResults();
            string[] guids = AssetDatabase.FindAssets("t:SpriteAtlas");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                if (atlas != null)
                {
                    AddResult(atlas, path);
                }
            }
        }

        private void ScanSelection()
        {
            ClearResults();
            UnityEngine.Object[] selectedObjects = Selection.objects;
            for (int i = 0; i < selectedObjects.Length; i++)
            {
                if (selectedObjects[i] is SpriteAtlas atlas)
                {
                    AddResult(atlas, AssetDatabase.GetAssetPath(atlas));
                }
            }

            if (_results.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "SpriteAtlas Compatibility Validator",
                    "Select one or more SpriteAtlas assets in the Project window.",
                    "OK");
            }
        }

        private void AddResult(SpriteAtlas atlas, string path)
        {
            SpriteAtlasPackingSettings packing = atlas.GetPackingSettings();
            SpriteAtlasTextureSettings textureSettings = atlas.GetTextureSettings();
            GetAtlasFormat(atlas, out string formatName, out bool isExplicitRgba32);
            Compatibility compatibility = ClassifyCompatibility(
                packing.enableRotation,
                packing.enableTightPacking,
                isExplicitRgba32,
                textureSettings.readable);
            IncrementCompatibilityCount(compatibility);

            _results.Add(new ValidationResult(
                path,
                atlas.name,
                formatName,
                textureSettings.readable,
                packing.enableRotation,
                packing.enableTightPacking,
                compatibility));
        }

        internal static Compatibility ClassifyCompatibility(
            bool rotationEnabled,
            bool tightPackingEnabled,
            bool isExplicitRgba32,
            bool readable)
        {
            if (rotationEnabled || tightPackingEnabled)
            {
                return Compatibility.UnsupportedPacking;
            }

            if (!isExplicitRgba32)
            {
                return Compatibility.ReadbackRequired;
            }

            return readable
                ? Compatibility.FastPathWithCpuFallback
                : Compatibility.FastPathCandidate;
        }

        private void IncrementCompatibilityCount(Compatibility compatibility)
        {
            switch (compatibility)
            {
                case Compatibility.FastPathCandidate:
                    _fastPathCount++;
                    break;
                case Compatibility.FastPathWithCpuFallback:
                    _cpuFallbackCount++;
                    break;
                case Compatibility.ReadbackRequired:
                    _readbackCount++;
                    break;
                default:
                    _unsupportedCount++;
                    break;
            }
        }

        private void GetAtlasFormat(SpriteAtlas atlas, out string formatName, out bool isExplicitRgba32)
        {
            string assetPath = AssetDatabase.GetAssetPath(atlas);
            SpriteAtlasImporter importer = AssetImporter.GetAtPath(assetPath) as SpriteAtlasImporter;
            if (importer != null)
            {
                TextureImporterPlatformSettings platformSettings = importer.GetPlatformSettings(GetPlatformName(_selectedPlatform));
                if (!platformSettings.overridden)
                {
                    platformSettings = importer.GetPlatformSettings("DefaultTexturePlatform");
                }

                formatName = platformSettings.format.ToString();
                isExplicitRgba32 = platformSettings.format == TextureImporterFormat.RGBA32;
                return;
            }

            int count = atlas.spriteCount;
            if (count > 0)
            {
                if (_spriteBuffer.Length < count)
                {
                    _spriteBuffer = new Sprite[count];
                }

                try
                {
                    int copiedCount = atlas.GetSprites(_spriteBuffer);
                    if (copiedCount > 0 && _spriteBuffer[0] != null && _spriteBuffer[0].texture != null)
                    {
                        TextureFormat runtimeFormat = _spriteBuffer[0].texture.format;
                        formatName = runtimeFormat.ToString();
                        isExplicitRgba32 = runtimeFormat == TextureFormat.RGBA32;
                        return;
                    }
                }
                finally
                {
                    Array.Clear(_spriteBuffer, 0, Mathf.Min(count, _spriteBuffer.Length));
                }
            }

            formatName = "Unknown";
            isExplicitRgba32 = false;
        }

        private void ClearResults()
        {
            _results.Clear();
            _fastPathCount = 0;
            _cpuFallbackCount = 0;
            _readbackCount = 0;
            _unsupportedCount = 0;
        }

        private void ClearSpriteBuffer()
        {
            if (_spriteBuffer != null)
            {
                Array.Clear(_spriteBuffer, 0, _spriteBuffer.Length);
            }
        }

        private static string GetPlatformName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneOSX:
                case BuildTarget.StandaloneLinux64:
                    return "Standalone";
                case BuildTarget.iOS:
                    return "iPhone";
                case BuildTarget.Android:
                    return "Android";
                case BuildTarget.WebGL:
                    return "WebGL";
                default:
                    return "DefaultTexturePlatform";
            }
        }

        [MenuItem("Assets/CycloneGames/Validate SpriteAtlas Compatibility", true)]
        private static bool CanValidateSelection()
        {
            return Selection.activeObject is SpriteAtlas;
        }

        [MenuItem("Assets/CycloneGames/Validate SpriteAtlas Compatibility")]
        private static void ValidateSelectionFromMenu()
        {
            SpriteAtlasFormatValidator window = GetWindow<SpriteAtlasFormatValidator>();
            window.Show();
            window.ScanSelection();
        }
    }
}
