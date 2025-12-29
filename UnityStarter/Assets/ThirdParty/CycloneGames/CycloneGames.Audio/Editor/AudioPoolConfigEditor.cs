// Copyright (c) CycloneGames
// Licensed under the MIT License.

using UnityEditor;
using UnityEngine;
using CycloneGames.Audio.Runtime;

namespace CycloneGames.Audio.Editor
{
    [CustomEditor(typeof(AudioPoolConfig))]
    public class AudioPoolConfigEditor : UnityEditor.Editor
    {
        private static string[] allConfigGuids;
        private static bool hasCheckedForDuplicates;

        // Foldout states
        private bool showDevicePreview = true;
        private bool showWebGL = true;
        private bool showMobile = true;
        private bool showDesktop = true;
        private bool showInitialSizes = true;
        private bool showExpansion = true;
        private bool showShrinking = true;
        private bool showRuntimeStats = true;

        // Colors
        private static readonly Color headerColor = new Color(0.2f, 0.6f, 0.9f);
        private static readonly Color webglColor = new Color(0.9f, 0.6f, 0.2f);
        private static readonly Color mobileColor = new Color(0.4f, 0.8f, 0.4f);
        private static readonly Color desktopColor = new Color(0.7f, 0.5f, 0.9f);
        private static readonly Color configColor = new Color(0.5f, 0.7f, 0.9f);
        private static readonly Color warningColor = new Color(0.9f, 0.7f, 0.3f);

        // Serialized properties
        private SerializedProperty webGLMaxPoolSize;
        private SerializedProperty mobileLowEndMaxPoolSize;
        private SerializedProperty mobileMidRangeMaxPoolSize;
        private SerializedProperty mobileHighEndMaxPoolSize;
        private SerializedProperty desktopLowEndMaxPoolSize;
        private SerializedProperty desktopMidRangeMaxPoolSize;
        private SerializedProperty desktopHighEndMaxPoolSize;
        private SerializedProperty webGLInitialPoolSize;
        private SerializedProperty mobileInitialPoolSize;
        private SerializedProperty desktopInitialPoolSize;
        private SerializedProperty expansionIncrement;
        private SerializedProperty shrinkIdleThreshold;
        private SerializedProperty shrinkUsageThreshold;
        private SerializedProperty shrinkInterval;

        // Cached GUIStyles to avoid per-frame allocations
        private GUIStyle _titleStyle;
        private GUIStyle _foldoutLabelStyle;
        private bool _stylesInitialized;

        private void OnEnable()
        {
            webGLMaxPoolSize = serializedObject.FindProperty("webGLMaxPoolSize");
            mobileLowEndMaxPoolSize = serializedObject.FindProperty("mobileLowEndMaxPoolSize");
            mobileMidRangeMaxPoolSize = serializedObject.FindProperty("mobileMidRangeMaxPoolSize");
            mobileHighEndMaxPoolSize = serializedObject.FindProperty("mobileHighEndMaxPoolSize");
            desktopLowEndMaxPoolSize = serializedObject.FindProperty("desktopLowEndMaxPoolSize");
            desktopMidRangeMaxPoolSize = serializedObject.FindProperty("desktopMidRangeMaxPoolSize");
            desktopHighEndMaxPoolSize = serializedObject.FindProperty("desktopHighEndMaxPoolSize");
            webGLInitialPoolSize = serializedObject.FindProperty("webGLInitialPoolSize");
            mobileInitialPoolSize = serializedObject.FindProperty("mobileInitialPoolSize");
            desktopInitialPoolSize = serializedObject.FindProperty("desktopInitialPoolSize");
            expansionIncrement = serializedObject.FindProperty("expansionIncrement");
            shrinkIdleThreshold = serializedObject.FindProperty("shrinkIdleThreshold");
            shrinkUsageThreshold = serializedObject.FindProperty("shrinkUsageThreshold");
            shrinkInterval = serializedObject.FindProperty("shrinkInterval");
            _stylesInitialized = false;
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };

            _foldoutLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Initialize cached styles
            InitializeStyles();

            // Check for duplicates
            CheckForDuplicates();

            var config = (AudioPoolConfig)target;
            bool isPlaying = Application.isPlaying;

            // Title
            DrawTitle();

            EditorGUILayout.Space(5);

            // Runtime notice
            if (isPlaying)
            {
                EditorGUILayout.HelpBox("🔒 Configuration is read-only during Play Mode.\nChanges must be made when not playing.", MessageType.Info);
                EditorGUILayout.Space(5);
            }

            // Duplicate warning
            DrawDuplicateWarning();

            // Device Preview Section
            DrawDevicePreview(config);

            EditorGUILayout.Space(5);

            // Wrap all config sections in disabled scope during play mode
            using (new EditorGUI.DisabledScope(isPlaying))
            {
                // Platform Configuration Sections
                DrawWebGLSection();
                DrawMobileSection();
                DrawDesktopSection();

                EditorGUILayout.Space(5);

                // Pool Behavior Sections
                DrawInitialSizesSection();
                DrawExpansionSection();
                DrawShrinkingSection();
            }

            // Runtime Statistics (play mode only)
            if (isPlaying)
            {
                EditorGUILayout.Space(10);
                DrawRuntimeStats();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTitle()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            EditorGUILayout.LabelField("Audio Pool Configuration", _titleStyle, GUILayout.Height(24));
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void CheckForDuplicates()
        {
            if (!hasCheckedForDuplicates || Event.current.type == EventType.Layout)
            {
                allConfigGuids = AssetDatabase.FindAssets("t:AudioPoolConfig");
                hasCheckedForDuplicates = true;
            }
        }

        private void DrawDuplicateWarning()
        {
            if (allConfigGuids != null && allConfigGuids.Length > 1)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                
                GUI.color = warningColor;
                EditorGUILayout.LabelField("⚠ Multiple Configs Detected", EditorStyles.boldLabel);
                GUI.color = Color.white;
                
                EditorGUILayout.LabelField($"Found {allConfigGuids.Length} AudioPoolConfig assets. Only one should exist.", EditorStyles.wordWrappedLabel);
                
                EditorGUILayout.Space(3);
                
                if (GUILayout.Button("Show All in Console"))
                {
                    foreach (var guid in allConfigGuids)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        Debug.Log($"AudioPoolConfig found at: {path}");
                    }
                }
                
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }
        }

        private void DrawDevicePreview(AudioPoolConfig config)
        {
            showDevicePreview = DrawFoldoutHeader("📊 Current Device Preview", showDevicePreview, headerColor);
            
            if (showDevicePreview)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                
                EditorGUI.indentLevel++;
                
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Device Tier", GUILayout.Width(120));
                    EditorGUILayout.LabelField(config.GetDeviceTierName(), EditorStyles.boldLabel);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Initial Pool", GUILayout.Width(120));
                    EditorGUILayout.LabelField($"{config.GetInitialPoolSizeForPlatform()} sources");
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Max Pool", GUILayout.Width(120));
                    EditorGUILayout.LabelField($"{config.GetMaxPoolSizeForDevice()} sources");
                    EditorGUILayout.EndHorizontal();

                    // Visual bar
                    EditorGUILayout.Space(5);
                    Rect barRect = EditorGUILayout.GetControlRect(false, 20);
                    barRect = EditorGUI.IndentedRect(barRect);
                    
                    float ratio = (float)config.GetInitialPoolSizeForPlatform() / config.GetMaxPoolSizeForDevice();
                    
                    EditorGUI.DrawRect(barRect, new Color(0.2f, 0.2f, 0.2f));
                    Rect fillRect = new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height);
                    EditorGUI.DrawRect(fillRect, new Color(0.3f, 0.7f, 0.4f));
                    
                    var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = Color.white }
                    };
                    EditorGUI.LabelField(barRect, $"{config.GetInitialPoolSizeForPlatform()} / {config.GetMaxPoolSizeForDevice()}", labelStyle);
                }
                
                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawWebGLSection()
        {
            showWebGL = DrawFoldoutHeader("🌐 WebGL Platform", showWebGL, webglColor);
            
            if (showWebGL)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.indentLevel++;
                
                EditorGUILayout.LabelField("Browser audio has strict limitations", EditorStyles.miniLabel);
                EditorGUILayout.Space(3);
                
                DrawSliderWithValue(webGLMaxPoolSize, "Max Pool Size", 8, 64);
                
                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawMobileSection()
        {
            showMobile = DrawFoldoutHeader("📱 Mobile Platforms (Android/iOS)", showMobile, mobileColor);
            
            if (showMobile)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.indentLevel++;
                
                EditorGUILayout.LabelField("Based on device RAM", EditorStyles.miniLabel);
                EditorGUILayout.Space(3);
                
                DrawTierRow("Low-End", "< 3 GB RAM", mobileLowEndMaxPoolSize, 16, 96);
                DrawTierRow("Mid-Range", "3-6 GB RAM", mobileMidRangeMaxPoolSize, 32, 128);
                DrawTierRow("High-End", "> 6 GB RAM", mobileHighEndMaxPoolSize, 48, 192);
                
                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawDesktopSection()
        {
            showDesktop = DrawFoldoutHeader("🖥️ Desktop Platforms", showDesktop, desktopColor);
            
            if (showDesktop)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.indentLevel++;
                
                EditorGUILayout.LabelField("Based on system RAM", EditorStyles.miniLabel);
                EditorGUILayout.Space(3);
                
                DrawTierRow("Low-End", "< 8 GB RAM", desktopLowEndMaxPoolSize, 64, 256);
                DrawTierRow("Mid-Range", "8-16 GB RAM", desktopMidRangeMaxPoolSize, 96, 384);
                DrawTierRow("High-End", "> 16 GB RAM", desktopHighEndMaxPoolSize, 128, 512);
                
                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawInitialSizesSection()
        {
            showInitialSizes = DrawFoldoutHeader("🚀 Initial Pool Sizes", showInitialSizes, configColor);
            
            if (showInitialSizes)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.indentLevel++;
                
                EditorGUILayout.LabelField("Pool size created at startup (minimum retained)", EditorStyles.miniLabel);
                EditorGUILayout.Space(3);
                
                DrawSliderWithValue(webGLInitialPoolSize, "WebGL", 8, 32);
                DrawSliderWithValue(mobileInitialPoolSize, "Mobile", 16, 64);
                DrawSliderWithValue(desktopInitialPoolSize, "Desktop", 32, 128);
                
                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawExpansionSection()
        {
            showExpansion = DrawFoldoutHeader("📈 Pool Expansion", showExpansion, configColor);
            
            if (showExpansion)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.indentLevel++;
                
                EditorGUILayout.LabelField("When pool runs out of sources", EditorStyles.miniLabel);
                EditorGUILayout.Space(3);
                
                DrawSliderWithValue(expansionIncrement, "Expansion Size", 4, 32);
                EditorGUILayout.LabelField("Sources added per expansion", EditorStyles.miniLabel);
                
                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawShrinkingSection()
        {
            showShrinking = DrawFoldoutHeader("📉 Pool Shrinking", showShrinking, configColor);
            
            if (showShrinking)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.indentLevel++;
                
                EditorGUILayout.LabelField("Release unused sources during idle periods", EditorStyles.miniLabel);
                EditorGUILayout.Space(3);
                
                DrawSliderWithValue(shrinkIdleThreshold, "Idle Threshold", 5f, 60f, "s");
                DrawSliderWithValue(shrinkUsageThreshold, "Usage Threshold", 0.1f, 0.8f, "", true);
                DrawSliderWithValue(shrinkInterval, "Shrink Interval", 0.5f, 5f, "s");
                
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Shrinking occurs when usage < threshold for idle duration", EditorStyles.miniLabel);
                
                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawRuntimeStats()
        {
            showRuntimeStats = DrawFoldoutHeader("⚡ Runtime Statistics", showRuntimeStats, new Color(0.9f, 0.4f, 0.4f));
            
            if (showRuntimeStats)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.indentLevel++;
                
                using (new EditorGUI.DisabledScope(true))
                {
                    DrawStatRow("Device Tier", AudioManager.PoolStats.DeviceTier);
                    DrawStatRow("Pool Size", $"{AudioManager.PoolStats.CurrentSize} / {AudioManager.PoolStats.MaxSize}");
                    DrawStatRow("In Use", $"{AudioManager.PoolStats.InUse}");
                    DrawStatRow("Available", $"{AudioManager.PoolStats.Available}");
                    
                    // Usage bar
                    EditorGUILayout.Space(3);
                    Rect barRect = EditorGUILayout.GetControlRect(false, 20);
                    barRect = EditorGUI.IndentedRect(barRect);
                    
                    float ratio = AudioManager.PoolStats.UsageRatio;
                    Color barColor = ratio < 0.5f ? new Color(0.3f, 0.7f, 0.4f) : 
                                     ratio < 0.8f ? new Color(0.9f, 0.7f, 0.3f) : 
                                     new Color(0.9f, 0.3f, 0.3f);
                    
                    EditorGUI.DrawRect(barRect, new Color(0.2f, 0.2f, 0.2f));
                    Rect fillRect = new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height);
                    EditorGUI.DrawRect(fillRect, barColor);
                    
                    var labelStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = Color.white }
                    };
                    EditorGUI.LabelField(barRect, $"{ratio:P0} Usage", labelStyle);
                    
                    EditorGUILayout.Space(5);
                    DrawStatRow("Peak Usage", $"{AudioManager.PoolStats.PeakUsage}");
                    DrawStatRow("Expansions", $"{AudioManager.PoolStats.TotalExpansions}");
                    DrawStatRow("Voice Steals", $"{AudioManager.PoolStats.TotalSteals}");
                }
                
                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
                
                // Auto-repaint
                Repaint();
            }
        }

        #region Utility Methods

        private bool DrawFoldoutHeader(string title, bool foldout, Color color)
        {
            EditorGUILayout.Space(3);
            
            Rect rect = EditorGUILayout.GetControlRect(false, 22);
            
            // Background
            Color bgColor = foldout ? color : new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f);
            EditorGUI.DrawRect(rect, bgColor);
            
            // Border
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), Color.black * 0.3f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Color.black * 0.3f);
            
            // Label - use cached style
            Rect labelRect = new Rect(rect.x + 20, rect.y, rect.width - 20, rect.height);
            EditorGUI.LabelField(labelRect, title, _foldoutLabelStyle);
            
            // Arrow
            string arrow = foldout ? "v" : ">";
            Rect arrowRect = new Rect(rect.x + 5, rect.y, 15, rect.height);
            EditorGUI.LabelField(arrowRect, arrow, _foldoutLabelStyle);
            
            // Click handling
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                foldout = !foldout;
                Event.current.Use();
            }
            
            return foldout;
        }

        private void DrawSliderWithValue(SerializedProperty prop, string label, int min, int max)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(120));
            prop.intValue = EditorGUILayout.IntSlider(prop.intValue, min, max);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSliderWithValue(SerializedProperty prop, string label, float min, float max, string suffix = "", bool isPercent = false)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(120));
            
            float newValue = EditorGUILayout.Slider(prop.floatValue, min, max);
            prop.floatValue = newValue;
            
            string displayValue = isPercent ? $"{newValue:P0}" : $"{newValue:F1}{suffix}";
            EditorGUILayout.LabelField(displayValue, GUILayout.Width(50));
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTierRow(string tier, string condition, SerializedProperty prop, int min, int max)
        {
            EditorGUILayout.BeginHorizontal();
            
            EditorGUILayout.LabelField(tier, EditorStyles.boldLabel, GUILayout.Width(80));
            EditorGUILayout.LabelField(condition, EditorStyles.miniLabel, GUILayout.Width(80));
            prop.intValue = EditorGUILayout.IntSlider(prop.intValue, min, max);
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(100));
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
        }

        #endregion
    }
}
