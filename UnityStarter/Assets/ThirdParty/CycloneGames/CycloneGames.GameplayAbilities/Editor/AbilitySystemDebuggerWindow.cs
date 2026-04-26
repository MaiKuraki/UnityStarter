using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Linq;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayAbilities.Editor
{
    /// <summary>
    /// Enhanced runtime debugger window for inspecting AbilitySystemComponent state.
    /// Shows active effects, attributes, abilities, tags, and pool statistics in real-time.
    /// Features:
    /// - Multi-target monitoring (compare multiple ASCs side-by-side)
    /// - Effect dependency visualization
    /// - Attribute change history tracking
    /// - Search and filtering capabilities
    /// - Advanced performance and GC monitoring
    /// - Persistent UI state
    /// </summary>
    public class AbilitySystemDebuggerWindow : EditorWindow
    {
        #region Constants & Theme

        // Unified color theme - easily customizable
        private static class ColorTheme
        {
            public static readonly Color EffectActive = new Color(0.25f, 0.65f, 0.35f, 1f);
            public static readonly Color EffectInhibited = new Color(0.6f, 0.6f, 0.2f, 1f);
            public static readonly Color EffectExpired = new Color(0.7f, 0.3f, 0.3f, 1f);
            public static readonly Color Tag = new Color(0.4f, 0.6f, 0.9f, 1f);
            public static readonly Color Attribute = new Color(0.9f, 0.75f, 0.3f, 1f);
            public static readonly Color CooldownActive = new Color(0.9f, 0.6f, 0.2f, 1f);
            public static readonly Color Ready = new Color(0.5f, 0.5f, 0.5f, 1f);
            public static readonly Color Immunity = new Color(0.85f, 0.35f, 0.55f, 1f);
            
            public static readonly Color BarBackground = new Color(0.15f, 0.15f, 0.15f, 1f);
            public static readonly Color BarFill = new Color(0.2f, 0.6f, 0.85f, 1f);
            public static readonly Color BarCooldown = new Color(0.9f, 0.55f, 0.15f, 1f);
            public static readonly Color BarHealth = new Color(0.25f, 0.75f, 0.35f, 1f);
            public static readonly Color BarPool = new Color(0.45f, 0.65f, 0.85f, 1f);
            
            public static readonly Color Warning = new Color(1f, 0.8f, 0.2f, 1f);
            public static readonly Color Error = new Color(1f, 0.3f, 0.3f, 1f);
            public static readonly Color Success = new Color(0.3f, 0.8f, 0.3f, 1f);
        }

        private static GUIStyle s_SectionHeader;
        private static GUIStyle s_BadgeStyle;
        private static GUIStyle s_MonoLabel;
        private static GUIStyle s_SearchBoxStyle;
        private static bool s_StylesInitialized;
        
        private const int MaxHistoryEntries = 200;
        private const int MaxAttributeHistoryPerAttribute = 50;
        private const float MinRepaintInterval = 0.02f;

        #endregion

        #region Data Structures

        /// <summary>
        /// Tracks historical changes to an attribute for analysis
        /// </summary>
        private struct AttributeHistory
        {
            public float Value;
            public float BaseValue;
            public double Timestamp;
        }

        /// <summary>
        /// Tracks effect application events
        /// </summary>
        private struct EffectEventRecord
        {
            public enum EventType { Applied, Removed, Modified, Inhibited, Restored }
            
            public EventType Type;
            public string EffectName;
            public double Timestamp;
            public float Duration;
            public int StackCount;
        }

        /// <summary>
        /// Multi-target comparison data
        /// </summary>
        private struct ComparisonTarget
        {
            public AbilitySystemComponent ASC;
            public GameObject Owner;
            public bool IsActive;
            public double LastUpdateTime;
        }

        #endregion

        #region State

        // Single target (primary)
        private GameObject selectedGameObject;
        private AbilitySystemComponent selectedASC;
        private Vector2 scrollPosition;

        // Multi-target support (comparison mode)
        private List<ComparisonTarget> comparisonTargets = new List<ComparisonTarget>();

        // Section foldout states - persistent
        private bool showEffects = true;
        private bool showAttributes = true;
        private bool showAbilities = true;
        private bool showTags = true;
        private bool showImmunityTags = false;
        private bool showPoolStats = false;
        private bool showEventLog = false;
        private bool showAttributeHistory = false;
        private bool showEffectRelations = false;
        private bool showPerformanceStats = false;

        // Per-effect detail foldouts
        private readonly HashSet<int> expandedEffects = new HashSet<int>();

        // Toolbar state
        private float refreshInterval = 0.1f;
        private double lastRefreshTime;
        private bool isPaused;

        // Search and filtering
        private string searchQuery = "";
        private bool showInhibitedOnly = false;
        private bool showExpiredOnly = false;

        // Event log and history tracking
        private readonly List<EffectEventRecord> effectHistory = new List<EffectEventRecord>(MaxHistoryEntries);
        private readonly Dictionary<string, List<AttributeHistory>> attributeHistories = 
            new Dictionary<string, List<AttributeHistory>>(32);
        private readonly List<string> eventLog = new List<string>(128);
        private const int MaxEventLogEntries = 64;
        private Vector2 eventLogScroll;
        private bool subscribedToEvents;

        // Performance monitoring
        private double lastGCCollectionTime;
        private long lastTotalMemory;
        private int frameCountSinceLastUpdate;
        private float avgFrameTime;

        // Caching system to reduce allocations
        private readonly StringBuilder sb = new StringBuilder(512);
        private readonly Dictionary<int, string> effectNameCache = new Dictionary<int, string>(64);
        private readonly Dictionary<string, float> attributeRowWidthCache = new Dictionary<string, float>(32);
        private double lastCacheUpdateTime;

        // ASC picker
        private readonly List<AbilitySystemComponent> sceneASCs = new List<AbilitySystemComponent>();
        private readonly List<string> sceneASCNames = new List<string>();
        private int selectedASCIndex = -1;
        private double lastASCScanTime;

        // UI state
        private int viewMode = 0; // 0: Single, 1: Comparison, 2: Network
        private Vector2 networkViewScroll;

        #endregion

        [MenuItem("Tools/CycloneGames/GameplayAbilities/GAS Debugger")]
        public static void ShowWindow()
        {
            var window = GetWindow<AbilitySystemDebuggerWindow>("GAS Debugger");
            window.minSize = new Vector2(600, 400);
        }

        [MenuItem("Tools/CycloneGames/GameplayAbilities/GAS Debugger (Multi-Target)")]
        public static void ShowWindowComparison()
        {
            var window = GetWindow<AbilitySystemDebuggerWindow>("GAS Debugger - Comparison");
            window.minSize = new Vector2(800, 500);
            window.viewMode = 1;
        }

        [MenuItem("Tools/CycloneGames/GameplayAbilities/GAS Overlay (Play Mode)")]
        public static void ToggleOverlay()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("GAS Overlay", "The runtime overlay is only available in Play Mode.", "OK");
                return;
            }
            Runtime.GASDebugOverlay.Toggle();
        }

        [MenuItem("Tools/CycloneGames/GameplayAbilities/GAS Overlay (Play Mode)", true)]
        private static bool ValidateToggleOverlay()
        {
            Menu.SetChecked("Tools/CycloneGames/GameplayAbilities/GAS Overlay (Play Mode)",
                EditorApplication.isPlaying && Runtime.GASDebugOverlay.IsActive);
            return true;
        }

        #region Lifecycle

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += OnEditorUpdate;
            LoadUIState();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.update -= OnEditorUpdate;
            UnsubscribeFromEvents();
            SaveUIState();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                UnsubscribeFromEvents();
                selectedASC = null;
                comparisonTargets.Clear();
                sceneASCs.Clear();
                sceneASCNames.Clear();
                eventLog.Clear();
                effectHistory.Clear();
                attributeHistories.Clear();
                selectedASCIndex = -1;
                expandedEffects.Clear();
            }
            Repaint();
        }

        private void OnSelectionChange()
        {
            if (EditorApplication.isPlaying)
            {
                TryFindASC();
                Repaint();
            }
        }

        private void OnEditorUpdate()
        {
            if (!EditorApplication.isPlaying || isPaused) return;
            
            frameCountSinceLastUpdate++;
            double timeSinceLastRefresh = EditorApplication.timeSinceStartup - lastRefreshTime;
            
            if (timeSinceLastRefresh > refreshInterval)
            {
                lastRefreshTime = EditorApplication.timeSinceStartup;
                avgFrameTime = (float)(timeSinceLastRefresh / frameCountSinceLastUpdate);
                frameCountSinceLastUpdate = 0;
                Repaint();
            }
        }

        #endregion

        #region Styles

        private static void EnsureStyles()
        {
            if (s_StylesInitialized) return;
            s_StylesInitialized = true;

            s_SectionHeader = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                padding = new RectOffset(2, 0, 4, 2),
                fontStyle = FontStyle.Bold
            };

            s_BadgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                padding = new RectOffset(4, 4, 1, 1),
                fontSize = 9
            };

            s_MonoLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                font = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font,
                richText = true
            };
            if (s_MonoLabel.font == null)
            {
                s_MonoLabel = new GUIStyle(EditorStyles.miniLabel) { richText = true };
            }

            s_SearchBoxStyle = new GUIStyle(EditorStyles.toolbarSearchField)
            {
                fontSize = 11,
                padding = new RectOffset(4, 4, 2, 2)
            };
        }

        #endregion

        #region Main GUI

        private void OnGUI()
        {
            EnsureStyles();
            DrawTopToolbar();

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to debug AbilitySystemComponents.", MessageType.Info);
                return;
            }

            // View mode selector
            DrawViewModeTabs();

            switch (viewMode)
            {
                case 0: DrawSingleTargetView(); break;
                case 1: DrawComparisonView(); break;
                case 2: DrawNetworkView(); break;
            }
        }

        private void DrawSingleTargetView()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            if (selectedASC == null)
            {
                DrawASCPicker();
            }
            else
            {
                DrawASCHeader();
                EditorGUILayout.Space(2);
                
                // Quick search bar
                DrawSearchBar();
                
                // Main sections
                DrawActiveEffectsSection();
                DrawAttributesSection();
                DrawAbilitiesSection();
                DrawTagsSection();
                DrawImmunityTagsSection();

                // Optional panels
                if (showAttributeHistory) DrawAttributeHistoryPanel();
                if (showEffectRelations) DrawEffectRelationsPanel();
                if (showPoolStats) DrawPoolStatistics();
                if (showPerformanceStats) DrawPerformanceStatistics();
                if (showEventLog) DrawEventLog();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawComparisonView()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.LabelField("Multi-Target Comparison (Beta)", s_SectionHeader);
            
            // Add targets
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add Target", EditorStyles.miniButton, GUILayout.Width(100)))
            {
                ScanSceneASCs();
                // Could show a dropdown or picker here
            }
            EditorGUILayout.EndHorizontal();

            if (comparisonTargets.Count == 0)
            {
                EditorGUILayout.HelpBox("No targets added for comparison. Click 'Add Target' to begin.", MessageType.Info);
            }
            else
            {
                DrawComparisonTable();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawNetworkView()
        {
            networkViewScroll = EditorGUILayout.BeginScrollView(networkViewScroll);

            EditorGUILayout.LabelField("Effect Network View (Experimental)", s_SectionHeader);
            EditorGUILayout.HelpBox("Shows source-effect-target relationships and dependencies.", MessageType.Info);

            if (selectedASC != null)
            {
                DrawEffectNetwork();
            }
            else
            {
                EditorGUILayout.HelpBox("Select an ASC to view its effect network.", MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawViewModeTabs()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            EditorGUI.BeginChangeCheck();
            int newMode = viewMode;
            
            if (GUILayout.Button("Single", viewMode == 0 ? EditorStyles.toolbarButton : EditorStyles.toolbarButton, GUILayout.Width(60)))
                newMode = 0;
            if (GUILayout.Button("Comparison", viewMode == 1 ? EditorStyles.toolbarButton : EditorStyles.toolbarButton, GUILayout.Width(85)))
                newMode = 1;
            if (GUILayout.Button("Network", viewMode == 2 ? EditorStyles.toolbarButton : EditorStyles.toolbarButton, GUILayout.Width(65)))
                newMode = 2;
            
            if (EditorGUI.EndChangeCheck())
                viewMode = newMode;
            
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Toolbar

        private void DrawTopToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Refresh button
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(55)))
            {
                ScanSceneASCs();
                TryFindASC();
                ClearCaches();
            }

            // Pause/Resume
            string pauseLabel = isPaused ? "▶" : "⏸";
            if (GUILayout.Button(pauseLabel, EditorStyles.toolbarButton, GUILayout.Width(30)))
            {
                isPaused = !isPaused;
            }

            GUILayout.Space(5);

            // Refresh rate
            EditorGUILayout.LabelField("Rate", GUILayout.Width(35));
            refreshInterval = EditorGUILayout.Slider(refreshInterval, MinRepaintInterval, 1f, GUILayout.Width(80));

            GUILayout.FlexibleSpace();

            // Section toggles
            DrawToolbarDivider();
            showAttributeHistory = GUILayout.Toggle(showAttributeHistory, "Hist", EditorStyles.toolbarButton, GUILayout.Width(40));
            showEffectRelations = GUILayout.Toggle(showEffectRelations, "Rel", EditorStyles.toolbarButton, GUILayout.Width(35));
            showPoolStats = GUILayout.Toggle(showPoolStats, "Pools", EditorStyles.toolbarButton, GUILayout.Width(45));
            showPerformanceStats = GUILayout.Toggle(showPerformanceStats, "Perf", EditorStyles.toolbarButton, GUILayout.Width(40));
            showEventLog = GUILayout.Toggle(showEventLog, "Log", EditorStyles.toolbarButton, GUILayout.Width(35));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSearchBar()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Filter", GUILayout.Width(40));
            
            EditorGUI.BeginChangeCheck();
            searchQuery = EditorGUILayout.TextField(searchQuery, s_SearchBoxStyle);
            if (EditorGUI.EndChangeCheck())
            {
                // Trigger filter refresh
            }

            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                searchQuery = "";
            }

            EditorGUILayout.EndHorizontal();

            // Filter options
            EditorGUILayout.BeginHorizontal();
            showInhibitedOnly = EditorGUILayout.ToggleLeft("Inhibited Only", showInhibitedOnly, GUILayout.Width(100));
            showExpiredOnly = EditorGUILayout.ToggleLeft("Expired Only", showExpiredOnly, GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawToolbarDivider()
        {
            GUILayout.Label("|", EditorStyles.miniLabel, GUILayout.Width(8));
        }

        #endregion

        #region ASC Discovery

        private void DrawASCPicker()
        {
            EditorGUILayout.HelpBox("Select a GameObject with an AbilitySystemComponent, or pick one from the scene.", MessageType.Info);

            EditorGUILayout.Space(4);

            // Manual object field
            EditorGUI.BeginChangeCheck();
            selectedGameObject = EditorGUILayout.ObjectField("Target GameObject", selectedGameObject, typeof(GameObject), true) as GameObject;
            if (EditorGUI.EndChangeCheck() && selectedGameObject != null)
            {
                TryFindASC();
            }

            EditorGUILayout.Space(4);

            // Scene ASC dropdown
            if (EditorApplication.timeSinceStartup - lastASCScanTime > 1.0)
            {
                ScanSceneASCs();
            }

            if (sceneASCs.Count > 0)
            {
                EditorGUILayout.LabelField("Scene ASCs", EditorStyles.boldLabel);
                int newIndex = EditorGUILayout.Popup("Pick ASC", selectedASCIndex, sceneASCNames.ToArray());
                if (newIndex != selectedASCIndex && newIndex >= 0 && newIndex < sceneASCs.Count)
                {
                    selectedASCIndex = newIndex;
                    selectedASC = sceneASCs[newIndex];
                    SubscribeToEvents();
                }
            }
            else
            {
                EditorGUILayout.LabelField("No AbilitySystemComponents found in scene.", EditorStyles.centeredGreyMiniLabel);
            }
        }

        private void ScanSceneASCs()
        {
            lastASCScanTime = EditorApplication.timeSinceStartup;
            sceneASCs.Clear();
            sceneASCNames.Clear();

            if (!EditorApplication.isPlaying) return;

            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb == null) continue;
                var asc = FindASCOnComponent(mb);
                if (asc != null && !sceneASCs.Contains(asc))
                {
                    sceneASCs.Add(asc);
                    string ownerName = asc.OwnerActor != null ? asc.OwnerActor.ToString() : mb.gameObject.name;
                    sceneASCNames.Add(ownerName);
                }
            }
        }

        private static AbilitySystemComponent FindASCOnComponent(MonoBehaviour mb)
        {
            var type = mb.GetType();
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.PropertyType == typeof(AbilitySystemComponent) && prop.CanRead)
                {
                    var asc = prop.GetValue(mb) as AbilitySystemComponent;
                    if (asc != null) return asc;
                }
            }
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType == typeof(AbilitySystemComponent))
                {
                    var asc = field.GetValue(mb) as AbilitySystemComponent;
                    if (asc != null) return asc;
                }
            }
            return null;
        }

        private void TryFindASC()
        {
            if (Selection.activeGameObject != null)
                selectedGameObject = Selection.activeGameObject;

            if (selectedGameObject == null) { selectedASC = null; return; }

            foreach (var mb in selectedGameObject.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                var asc = FindASCOnComponent(mb);
                if (asc != null)
                {
                    selectedASC = asc;
                    SubscribeToEvents();
                    return;
                }
            }
            selectedASC = null;
        }

        #endregion

        #region ASC Header

        private void DrawASCHeader()
        {
            EditorGUILayout.BeginHorizontal();

            string ownerName = selectedASC.OwnerActor?.ToString() ?? "(no owner)";
            EditorGUILayout.LabelField($"ASC: {ownerName}", s_SectionHeader, GUILayout.MinWidth(100));

            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(45)))
            {
                UnsubscribeFromEvents();
                selectedASC = null;
                selectedASCIndex = -1;
            }

            EditorGUILayout.EndHorizontal();

            // Summary line
            int effectCount = selectedASC.ActiveEffects?.Count ?? 0;
            int attrSetCount = selectedASC.AttributeSets?.Count ?? 0;
            int abilityCount = selectedASC.GetActivatableAbilities()?.Count ?? 0;
            sb.Clear();
            sb.Append("Effects: ").Append(effectCount)
              .Append("  |  Attr Sets: ").Append(attrSetCount)
              .Append("  |  Abilities: ").Append(abilityCount);
            EditorGUILayout.LabelField(sb.ToString(), EditorStyles.centeredGreyMiniLabel);

            DrawHorizontalLine();
        }

        #endregion

        #region Active Effects

        private void DrawActiveEffectsSection()
        {
            int count = selectedASC.ActiveEffects?.Count ?? 0;
            showEffects = EditorGUILayout.BeginFoldoutHeaderGroup(showEffects, $"Active Effects ({count})");

            if (showEffects && selectedASC.ActiveEffects != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                if (count == 0)
                {
                    EditorGUILayout.LabelField("No active effects", EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    for (int i = 0; i < selectedASC.ActiveEffects.Count; i++)
                    {
                        DrawEffectEntry(selectedASC.ActiveEffects[i], i);
                    }
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawEffectEntry(ActiveGameplayEffect effect, int index)
        {
            if (effect?.Spec?.Def == null) return;

            // Apply filters
            if (!MatchesSearchFilter(effect.Spec.Def.Name)) return;
            if (showInhibitedOnly && !effect.IsInhibited) return;
            if (showExpiredOnly && !effect.IsExpired) return;

            // Pick background color based on state
            var originalBg = GUI.backgroundColor;
            if (effect.IsExpired)
                GUI.backgroundColor = ColorTheme.EffectExpired;
            else if (effect.IsInhibited)
                GUI.backgroundColor = ColorTheme.EffectInhibited;
            else
                GUI.backgroundColor = ColorTheme.EffectActive;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalBg;

            // --- Header row: Name + badges ---
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(effect.Spec.Def.Name, EditorStyles.boldLabel);

            // Duration policy badge
            DrawBadge(GetDurationLabel(effect.Spec.Def.DurationPolicy), GetDurationColor(effect.Spec.Def.DurationPolicy));

            // Inhibited badge
            if (effect.IsInhibited)
                DrawBadge("INHIBITED", ColorTheme.EffectInhibited);

            // Stack badge
            if (effect.StackCount > 1)
                DrawBadge($"x{effect.StackCount}", new Color(0.5f, 0.4f, 0.7f));

            // Source indicator
            if (effect.Spec.Source != null && effect.Spec.Source != selectedASC)
            {
                DrawBadge("FROM", ColorTheme.Tag);
            }

            EditorGUILayout.EndHorizontal();

            // --- Duration bar (for HasDuration) ---
            if (effect.Spec.Def.DurationPolicy == EDurationPolicy.HasDuration)
            {
                float progress = effect.Spec.Duration > 0 ? effect.TimeRemaining / effect.Spec.Duration : 0f;
                sb.Clear();
                sb.Append(effect.TimeRemaining.ToString("F1")).Append("s / ").Append(effect.Spec.Duration.ToString("F1")).Append('s');
                DrawProgressBar(progress, sb.ToString(), ColorTheme.BarFill);
            }
            else if (effect.Spec.Def.DurationPolicy == EDurationPolicy.Infinite)
            {
                EditorGUILayout.LabelField("\u221E Infinite Duration", EditorStyles.miniLabel);
            }

            // --- Stacking info ---
            if (effect.Spec.Def.Stacking.Type != EGameplayEffectStackingType.None)
            {
                sb.Clear();
                sb.Append("Stacking: ").Append(effect.Spec.Def.Stacking.Type)
                  .Append(" (").Append(effect.StackCount).Append('/').Append(effect.Spec.Def.Stacking.Limit).Append(')');
                EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);
            }

            // --- Expandable detail ---
            bool isExpanded = expandedEffects.Contains(index);
            bool newExpanded = EditorGUILayout.Foldout(isExpanded, "Details", true);
            if (newExpanded != isExpanded)
            {
                if (newExpanded) expandedEffects.Add(index);
                else expandedEffects.Remove(index);
            }

            if (newExpanded)
            {
                EditorGUI.indentLevel++;
                DrawEffectDetails(effect);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawEffectDetails(ActiveGameplayEffect effect)
        {
            var def = effect.Spec.Def;

            // Modifiers
            if (def.Modifiers != null && def.Modifiers.Count > 0)
            {
                EditorGUILayout.LabelField("Modifiers:", EditorStyles.miniLabel);
                EditorGUI.indentLevel++;
                for (int i = 0; i < def.Modifiers.Count; i++)
                {
                    var mod = def.Modifiers[i];
                    sb.Clear();
                    sb.Append(mod.AttributeName).Append("  ");
                    sb.Append(GetOperatorSymbol(mod.Operation));

                    // Show pre-calculated magnitude from spec if available
                    if (effect.Spec.ModifierMagnitudes != null && i < effect.Spec.ModifierMagnitudes.Length)
                    {
                        sb.Append(' ').Append(effect.Spec.ModifierMagnitudes[i].ToString("F2"));
                    }
                    else
                    {
                        sb.Append(" (ScalableFloat base=").Append(mod.Magnitude.BaseValue.ToString("F2")).Append(')');
                    }

                    var origColor = GUI.contentColor;
                    GUI.contentColor = ColorTheme.Attribute;
                    EditorGUILayout.LabelField(sb.ToString(), s_MonoLabel);
                    GUI.contentColor = origColor;
                }
                EditorGUI.indentLevel--;
            }

            // Source
            if (effect.Spec.Source != null)
            {
                string sourceName = effect.Spec.Source.OwnerActor?.ToString() ?? "(unknown)";
                EditorGUILayout.LabelField($"Source: {sourceName}", EditorStyles.miniLabel);
            }

            // Period
            if (def.Period > 0)
            {
                EditorGUILayout.LabelField($"Period: {def.Period:F2}s  (Executions: {(int)(effect.TimeRemaining / def.Period)})", EditorStyles.miniLabel);
            }

            // Level
            if (effect.Spec.Level > 1)
            {
                EditorGUILayout.LabelField($"Level: {effect.Spec.Level}", EditorStyles.miniLabel);
            }

            // Remaining time info
            sb.Clear();
            sb.Append("Time Remaining: ").Append(effect.TimeRemaining.ToString("F1")).Append("s");
            EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);

            // Granted Tags
            if (def.GrantedTags != null && !def.GrantedTags.IsEmpty)
            {
                sb.Clear();
                sb.Append("Grants Tags: ");
                foreach (var tag in def.GrantedTags.GetTags())
                {
                    sb.Append(tag.Name).Append(", ");
                }
                if (sb.Length > 2) sb.Length -= 2;
                EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);
            }

            // Debug remove button (uses tag-based removal when possible)
            var grantedTags = def.GrantedTags;
            if (grantedTags != null && !grantedTags.IsEmpty)
            {
                EditorGUILayout.Space(2);
                if (GUILayout.Button("Remove (by GrantedTags)", EditorStyles.miniButton))
                {
                    selectedASC.RemoveActiveEffectsWithGrantedTags(grantedTags);
                }
            }
        }

        #endregion

        #region Attributes

        private void DrawAttributesSection()
        {
            int setCount = selectedASC.AttributeSets?.Count ?? 0;
            showAttributes = EditorGUILayout.BeginFoldoutHeaderGroup(showAttributes, $"Attributes ({setCount} sets)");

            if (showAttributes && selectedASC.AttributeSets != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                if (setCount == 0)
                {
                    EditorGUILayout.LabelField("No attribute sets", EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    foreach (var attrSet in selectedASC.AttributeSets)
                    {
                        EditorGUILayout.LabelField(attrSet.GetType().Name, EditorStyles.boldLabel);
                        EditorGUI.indentLevel++;

                        foreach (var attr in attrSet.GetAttributes())
                        {
                            DrawAttributeRow(attr);
                        }

                        EditorGUI.indentLevel--;
                        EditorGUILayout.Space(2);
                    }
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawAttributeRow(GameplayAttribute attr)
        {
            EditorGUILayout.BeginHorizontal();

            // Values text (right-aligned, fixed width)
            sb.Clear();
            sb.Append(attr.CurrentValue.ToString("F1"));
            if (System.Math.Abs(attr.BaseValue - attr.CurrentValue) > 0.001f)
            {
                sb.Append(" / ").Append(attr.BaseValue.ToString("F1"));
            }
            string valueText = sb.ToString();

            // Name — use remaining width after bar and value
            float valueWidth = 90;
            float barFraction = 0.4f;
            float totalWidth = EditorGUIUtility.currentViewWidth - 40;
            float nameWidth = totalWidth * (1f - barFraction) - valueWidth;
            if (nameWidth < 80) nameWidth = 80;
            float barWidth = totalWidth * barFraction;
            if (barWidth < 40) barWidth = 40;

            var origColor = GUI.contentColor;
            GUI.contentColor = ColorTheme.Attribute;
            EditorGUILayout.LabelField(attr.Name, s_MonoLabel, GUILayout.Width(nameWidth));
            GUI.contentColor = origColor;

            // Value bar
            float displayMax = Mathf.Max(attr.BaseValue, attr.CurrentValue, 1f);
            float fill = displayMax > 0 ? attr.CurrentValue / displayMax : 0f;
            Color barColor = attr.CurrentValue >= attr.BaseValue ? ColorTheme.BarHealth : ColorTheme.EffectExpired;

            Rect barRect = EditorGUILayout.GetControlRect(false, 14, GUILayout.Width(barWidth));
            DrawMiniBar(barRect, fill, barColor);

            // Value label
            EditorGUILayout.LabelField(valueText, EditorStyles.miniLabel, GUILayout.Width(valueWidth));

            EditorGUILayout.EndHorizontal();

            // Track history (low frequency to reduce GC)
            if (EditorApplication.timeSinceStartup % 0.5 < MinRepaintInterval)
            {
                TrackAttributeHistory(attr);
            }
        }

        #endregion

        #region Abilities

        private void DrawAbilitiesSection()
        {
            var abilities = selectedASC.GetActivatableAbilities();
            int count = abilities?.Count ?? 0;
            showAbilities = EditorGUILayout.BeginFoldoutHeaderGroup(showAbilities, $"Abilities ({count})");

            if (showAbilities && abilities != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                if (count == 0)
                {
                    EditorGUILayout.LabelField("No granted abilities", EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    foreach (var spec in abilities)
                    {
                        DrawAbilityEntry(spec);
                    }
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawAbilityEntry(GameplayAbilitySpec spec)
        {
            if (spec?.Ability == null) return;

            EditorGUILayout.BeginHorizontal();

            // Name (use Ability.Name if available, fallback to type name)
            string displayName = !string.IsNullOrEmpty(spec.Ability.Name)
                ? spec.Ability.Name
                : spec.Ability.GetType().Name;

            EditorGUILayout.LabelField(displayName, EditorStyles.miniLabel);

            // Level badge
            if (spec.Level > 0)
            {
                DrawBadge($"Lv.{spec.Level}", ColorTheme.Ready);
            }

            // Status badge
            if (spec.IsActive)
            {
                DrawBadge("ACTIVE", ColorTheme.EffectActive);
            }
            else if (selectedASC.IsAbilityOnCooldown(spec.Ability))
            {
                float cdRemaining = selectedASC.GetCooldownTimeRemaining(spec.Ability);
                DrawBadge($"CD {cdRemaining:F1}s", ColorTheme.CooldownActive);
            }
            else
            {
                DrawBadge("READY", ColorTheme.Ready);
            }

            // Instancing policy
            DrawBadge(GetInstancingLabel(spec.Ability.InstancingPolicy), new Color(0.4f, 0.4f, 0.5f));

            EditorGUILayout.EndHorizontal();

            // Cooldown bar
            if (selectedASC.GetCooldownInfo(spec.Ability, out float timeRemaining, out float totalDuration) && totalDuration > 0)
            {
                float cdProgress = timeRemaining / totalDuration;
                EditorGUI.indentLevel++;
                sb.Clear();
                sb.Append("Cooldown: ").Append(timeRemaining.ToString("F1")).Append("s / ").Append(totalDuration.ToString("F1")).Append('s');
                DrawProgressBar(cdProgress, sb.ToString(), ColorTheme.BarCooldown);
                EditorGUI.indentLevel--;
            }
        }

        #endregion

        #region Tags

        private void DrawTagsSection()
        {
            showTags = EditorGUILayout.BeginFoldoutHeaderGroup(showTags, "Combined Tags");

            if (showTags)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                var tags = selectedASC.CombinedTags;
                if (tags != null)
                {
                    bool hasAny = false;
                    foreach (var tag in tags.GetExplicitTags())
                    {
                        if (!MatchesSearchFilter(tag.Name)) continue;
                        
                        hasAny = true;
                        int count = tags.GetExplicitTagCount(tag);

                        EditorGUILayout.BeginHorizontal();
                        var origColor = GUI.contentColor;
                        GUI.contentColor = ColorTheme.Tag;
                        EditorGUILayout.LabelField(tag.Name, s_MonoLabel);
                        GUI.contentColor = origColor;

                        if (count > 1)
                        {
                            DrawBadge($"x{count}", ColorTheme.Tag);
                        }

                        EditorGUILayout.EndHorizontal();
                    }
                    if (!hasAny)
                    {
                        EditorGUILayout.LabelField("No tags", EditorStyles.centeredGreyMiniLabel);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("No tags", EditorStyles.centeredGreyMiniLabel);
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawImmunityTagsSection()
        {
            var immunityTags = selectedASC.ImmunityTags;
            if (immunityTags == null || immunityTags.IsEmpty) return;

            showImmunityTags = EditorGUILayout.BeginFoldoutHeaderGroup(showImmunityTags, "Immunity Tags");

            if (showImmunityTags)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                foreach (var tag in immunityTags.GetTags())
                {
                    if (!MatchesSearchFilter(tag.Name)) continue;
                    
                    var origColor = GUI.contentColor;
                    GUI.contentColor = ColorTheme.Immunity;
                    EditorGUILayout.LabelField(tag.Name, s_MonoLabel);
                    GUI.contentColor = origColor;
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        #endregion

        #region Pool Statistics

        private void DrawPoolStatistics()
        {
            EditorGUILayout.Space(4);
            DrawHorizontalLine();
            EditorGUILayout.LabelField("Pool Statistics", s_SectionHeader);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DrawPoolCard("EffectSpec", GASPool<GameplayEffectSpec>.Shared.GetStatistics());
            DrawPoolCard("ActiveEffect", GASPool<ActiveGameplayEffect>.Shared.GetStatistics());
            DrawPoolCard("Context", GASPool<GameplayEffectContext>.Shared.GetStatistics());
            DrawPoolCard("AbilitySpec", GASPool<GameplayAbilitySpec>.Shared.GetStatistics());

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Warm All (32)", EditorStyles.miniButton))
            {
                GASPool<GameplayEffectSpec>.Shared.Warm(32);
                GASPool<ActiveGameplayEffect>.Shared.Warm(32);
                GASPool<GameplayEffectContext>.Shared.Warm(32);
                GASPool<GameplayAbilitySpec>.Shared.Warm(32);
            }
            if (GUILayout.Button("Shrink All", EditorStyles.miniButton))
            {
                GASPoolRegistry.AggressiveShrinkAll();
            }
            if (GUILayout.Button("Reset Stats", EditorStyles.miniButton))
            {
                GASPool<GameplayEffectSpec>.Shared.ResetStatistics();
                GASPool<ActiveGameplayEffect>.Shared.ResetStatistics();
                GASPool<GameplayEffectContext>.Shared.ResetStatistics();
                GASPool<GameplayAbilitySpec>.Shared.ResetStatistics();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawPoolCard(string name, GASPoolStatistics stats)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Header
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(name, EditorStyles.boldLabel, GUILayout.Width(100));

            // Hit rate bar
            float hitRate = stats.HitRate;
            Color hitColor = hitRate > 0.9f ? ColorTheme.BarHealth : hitRate > 0.5f ? ColorTheme.CooldownActive : ColorTheme.EffectExpired;
            sb.Clear();
            sb.Append("Hit: ").Append((hitRate * 100f).ToString("F0")).Append('%');
            Rect hitRect = EditorGUILayout.GetControlRect(false, 14, GUILayout.Width(100));
            DrawMiniBar(hitRect, hitRate, hitColor);
            EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel, GUILayout.Width(60));

            EditorGUILayout.EndHorizontal();

            // Stats row
            sb.Clear();
            sb.Append("Pool: ").Append(stats.PoolSize)
              .Append("  Active: ").Append(stats.ActiveCount)
              .Append("  Peak: ").Append(stats.PeakActive)
              .Append("  Gets: ").Append(stats.TotalGets)
              .Append("  Misses: ").Append(stats.TotalMisses);
            EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);

            // Capacity row
            sb.Clear();
            sb.Append("Target Cap: ").Append(stats.TargetCapacity)
              .Append("  Peak Cap: ").Append(stats.PeakCapacity)
              .Append("  Max Cap: ").Append(stats.MaxCapacity)
              .Append("  Discards: ").Append(stats.TotalDiscards);
            EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Event Log

        private void SubscribeToEvents()
        {
            UnsubscribeFromEvents();
            if (selectedASC == null) return;

            selectedASC.OnGameplayEffectAppliedToSelf += OnEffectApplied;
            selectedASC.OnGameplayEffectRemovedFromSelf += OnEffectRemoved;
            selectedASC.OnAbilityActivated += OnAbilityActivated;
            selectedASC.OnAbilityEndedEvent += OnAbilityEnded;
            subscribedToEvents = true;
        }

        private void UnsubscribeFromEvents()
        {
            if (!subscribedToEvents || selectedASC == null) return;

            selectedASC.OnGameplayEffectAppliedToSelf -= OnEffectApplied;
            selectedASC.OnGameplayEffectRemovedFromSelf -= OnEffectRemoved;
            selectedASC.OnAbilityActivated -= OnAbilityActivated;
            selectedASC.OnAbilityEndedEvent -= OnAbilityEnded;
            subscribedToEvents = false;
        }

        private void OnEffectApplied(ActiveGameplayEffect effect)
        {
            AddLogEntry($"[+Effect] {effect?.Spec?.Def?.Name ?? "?"}");
        }

        private void OnEffectRemoved(ActiveGameplayEffect effect)
        {
            AddLogEntry($"[-Effect] {effect?.Spec?.Def?.Name ?? "?"}");
        }

        private void OnAbilityActivated(GameplayAbility ability)
        {
            AddLogEntry($"[>Ability] {ability?.Name ?? ability?.GetType().Name ?? "?"}");
        }

        private void OnAbilityEnded(GameplayAbility ability)
        {
            AddLogEntry($"[<Ability] {ability?.Name ?? ability?.GetType().Name ?? "?"}");
        }

        private void AddLogEntry(string message)
        {
            sb.Clear();
            sb.Append('[').Append(System.DateTime.Now.ToString("HH:mm:ss.ff")).Append("] ").Append(message);

            if (eventLog.Count >= MaxEventLogEntries)
                eventLog.RemoveAt(0);
            eventLog.Add(sb.ToString());
        }

        private void DrawEventLog()
        {
            EditorGUILayout.Space(4);
            DrawHorizontalLine();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Event Log", s_SectionHeader);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(50)))
            {
                eventLog.Clear();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (eventLog.Count == 0)
            {
                EditorGUILayout.LabelField("No events recorded.", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                eventLogScroll = EditorGUILayout.BeginScrollView(eventLogScroll, GUILayout.MaxHeight(150));
                // Draw newest first
                for (int i = eventLog.Count - 1; i >= 0; i--)
                {
                    string entry = eventLog[i];
                    Color logColor = Color.white;
                    if (entry.Contains("[+Effect]")) logColor = ColorTheme.EffectActive;
                    else if (entry.Contains("[-Effect]")) logColor = ColorTheme.EffectExpired;
                    else if (entry.Contains("[>Ability]")) logColor = ColorTheme.BarFill;
                    else if (entry.Contains("[<Ability]")) logColor = ColorTheme.Ready;

                    var origColor = GUI.contentColor;
                    GUI.contentColor = logColor;
                    EditorGUILayout.LabelField(entry, EditorStyles.miniLabel);
                    GUI.contentColor = origColor;
                }
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Advanced Panels

        private void DrawAttributeHistoryPanel()
        {
            EditorGUILayout.Space(4);
            DrawHorizontalLine();
            EditorGUILayout.LabelField("Attribute Change History", s_SectionHeader);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (attributeHistories.Count == 0)
            {
                EditorGUILayout.LabelField("No history recorded yet.", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                foreach (var kvp in attributeHistories.OrderBy(x => x.Key))
                {
                    string attrName = kvp.Key;
                    var history = kvp.Value;
                    if (history.Count == 0) continue;

                    EditorGUILayout.LabelField(attrName, EditorStyles.boldLabel);
                    
                    // Show recent changes
                    int startIdx = Mathf.Max(0, history.Count - 5);
                    for (int i = startIdx; i < history.Count; i++)
                    {
                        var record = history[i];
                        sb.Clear();
                        sb.Append(record.Value.ToString("F1"));
                        if (System.Math.Abs(record.BaseValue - record.Value) > 0.001f)
                            sb.Append(" (base: ").Append(record.BaseValue.ToString("F1")).Append(")");
                        sb.Append(" @ ").Append(record.Timestamp.ToString("F1"));
                        EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);
                    }
                    EditorGUILayout.Space(2);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawEffectRelationsPanel()
        {
            EditorGUILayout.Space(4);
            DrawHorizontalLine();
            EditorGUILayout.LabelField("Effect Relations & Dependencies", s_SectionHeader);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (selectedASC?.ActiveEffects == null || selectedASC.ActiveEffects.Count == 0)
            {
                EditorGUILayout.LabelField("No active effects.", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                // Group effects by source
                var effectsBySource = new Dictionary<AbilitySystemComponent, List<ActiveGameplayEffect>>();
                foreach (var effect in selectedASC.ActiveEffects)
                {
                    var source = effect.Spec.Source ?? selectedASC;
                    if (!effectsBySource.ContainsKey(source))
                        effectsBySource[source] = new List<ActiveGameplayEffect>();
                    effectsBySource[source].Add(effect);
                }

                foreach (var kvp in effectsBySource)
                {
                    string sourceName = kvp.Key == selectedASC ? "(Self)" : kvp.Key.OwnerActor?.ToString() ?? "(Unknown)";
                    EditorGUILayout.LabelField($"From: {sourceName}", EditorStyles.boldLabel);
                    
                    EditorGUI.indentLevel++;
                    foreach (var effect in kvp.Value)
                    {
                        sb.Clear();
                        sb.Append("→ ").Append(effect.Spec.Def.Name);
                        if (effect.IsInhibited) sb.Append(" [INHIBITED]");
                        EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);
                    }
                    EditorGUI.indentLevel--;
                    EditorGUILayout.Space(2);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPerformanceStatistics()
        {
            EditorGUILayout.Space(4);
            DrawHorizontalLine();
            EditorGUILayout.LabelField("Performance Monitoring", s_SectionHeader);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Frame time
            sb.Clear();
            sb.Append("Avg Frame Time: ").Append(avgFrameTime.ToString("F2")).Append("ms");
            EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);

            // Memory stats
            long currentMemory = System.GC.GetTotalMemory(false);
            long memoryDelta = currentMemory - lastTotalMemory;
            sb.Clear();
            sb.Append("Heap: ").Append((currentMemory / (1024f * 1024f)).ToString("F1")).Append("MB");
            if (memoryDelta != 0)
                sb.Append(" (Δ").Append((memoryDelta / 1024f).ToString("F1")).Append("KB)");
            EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);

            // GC info
            sb.Clear();
            sb.Append("GC Count: ");
            for (int i = 0; i < 3; i++)
            {
                sb.Append("Gen").Append(i).Append("=").Append(System.GC.CollectionCount(i));
                if (i < 2) sb.Append(" | ");
            }
            EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);

            // Cache info
            sb.Clear();
            sb.Append("String Cache: ").Append(effectNameCache.Count)
              .Append(" | Width Cache: ").Append(attributeRowWidthCache.Count)
              .Append(" | Attr History: ").Append(attributeHistories.Count);
            EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);

            lastTotalMemory = currentMemory;

            EditorGUILayout.EndVertical();
        }

        private void DrawComparisonTable()
        {
            EditorGUILayout.LabelField("Active Targets", EditorStyles.boldLabel);
            
            for (int i = 0; i < comparisonTargets.Count; i++)
            {
                var target = comparisonTargets[i];
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                EditorGUILayout.LabelField($"{i + 1}. {target.Owner?.name ?? "Unknown"}", GUILayout.MinWidth(150));
                
                if (target.ASC != null)
                {
                    int effectCount = target.ASC.ActiveEffects?.Count ?? 0;
                    EditorGUILayout.LabelField($"Effects: {effectCount}", GUILayout.Width(100));
                }

                if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    comparisonTargets.RemoveAt(i);
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawEffectNetwork()
        {
            EditorGUILayout.LabelField("Effect Network Visualization", EditorStyles.boldLabel);
            
            if (selectedASC?.ActiveEffects == null)
            {
                EditorGUILayout.HelpBox("No active effects to display.", MessageType.Info);
                return;
            }

            // Count effects by type
            int instant = 0, duration = 0, infinite = 0;
            int sourceCount = new HashSet<AbilitySystemComponent>(
                selectedASC.ActiveEffects.Select(e => e.Spec.Source).Where(s => s != null)
            ).Count;

            foreach (var effect in selectedASC.ActiveEffects)
            {
                switch (effect.Spec.Def.DurationPolicy)
                {
                    case EDurationPolicy.Instant: instant++; break;
                    case EDurationPolicy.HasDuration: duration++; break;
                    case EDurationPolicy.Infinite: infinite++; break;
                }
            }

            // Network stats
            EditorGUILayout.LabelField("Network Statistics:", EditorStyles.boldLabel);
            sb.Clear();
            sb.Append("Total Effects: ").Append(selectedASC.ActiveEffects.Count)
              .Append(" | Instant: ").Append(instant)
              .Append(" | Duration: ").Append(duration)
              .Append(" | Infinite: ").Append(infinite)
              .Append(" | Sources: ").Append(sourceCount);
            EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Dominant Sources:", EditorStyles.boldLabel);

            var sourceGroups = selectedASC.ActiveEffects
                .GroupBy(e => e.Spec.Source?.OwnerActor?.ToString() ?? "(Self)")
                .OrderByDescending(g => g.Count())
                .Take(5);

            foreach (var group in sourceGroups)
            {
                sb.Clear();
                sb.Append(group.Key).Append(": ").Append(group.Count()).Append(" effects");
                EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);
            }
        }

        #endregion

        #region Drawing Helpers

        private static void DrawBadge(string text, Color color)
        {
            var origBg = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUILayout.Label(text, s_BadgeStyle, GUILayout.Height(16));
            GUI.backgroundColor = origBg;
        }

        private void DrawProgressBar(float progress, string label, Color fillColor)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 16);
            EditorGUI.DrawRect(rect, ColorTheme.BarBackground);

            Rect fillRect = new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(progress), rect.height);
            EditorGUI.DrawRect(fillRect, fillColor);

            // Text on bar
            var origColor = GUI.contentColor;
            GUI.contentColor = Color.white;
            GUI.Label(rect, label, s_BadgeStyle);
            GUI.contentColor = origColor;
        }

        private static void DrawMiniBar(Rect rect, float fill, Color color)
        {
            EditorGUI.DrawRect(rect, ColorTheme.BarBackground);
            Rect fillRect = new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height);
            EditorGUI.DrawRect(fillRect, color);
        }

        private static void DrawHorizontalLine()
        {
            EditorGUILayout.Space(2);
            Rect lineRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(lineRect, new Color(0.3f, 0.3f, 0.3f, 1f));
            EditorGUILayout.Space(2);
        }

        private static string GetDurationLabel(EDurationPolicy policy)
        {
            switch (policy)
            {
                case EDurationPolicy.Instant: return "Instant";
                case EDurationPolicy.HasDuration: return "Duration";
                case EDurationPolicy.Infinite: return "Infinite";
                default: return "?";
            }
        }

        private static Color GetDurationColor(EDurationPolicy policy)
        {
            switch (policy)
            {
                case EDurationPolicy.Instant: return new Color(0.4f, 0.7f, 0.9f);
                case EDurationPolicy.HasDuration: return new Color(0.3f, 0.6f, 0.4f);
                case EDurationPolicy.Infinite: return new Color(0.6f, 0.4f, 0.7f);
                default: return Color.gray;
            }
        }

        private static string GetOperatorSymbol(EAttributeModifierOperation op)
        {
            switch (op)
            {
                case EAttributeModifierOperation.Add: return "+";
                case EAttributeModifierOperation.Multiply: return "\u00D7";
                case EAttributeModifierOperation.Division: return "\u00F7";
                case EAttributeModifierOperation.Override: return "=";
                default: return "?";
            }
        }

        private static string GetInstancingLabel(EGameplayAbilityInstancingPolicy policy)
        {
            switch (policy)
            {
                case EGameplayAbilityInstancingPolicy.NonInstanced: return "CDO";
                case EGameplayAbilityInstancingPolicy.InstancedPerActor: return "Actor";
                case EGameplayAbilityInstancingPolicy.InstancedPerExecution: return "Exec";
                default: return "?";
            }
        }

        #endregion

        #region Utility Methods

        private bool MatchesSearchFilter(string text)
        {
            if (string.IsNullOrEmpty(searchQuery)) return true;
            return text.IndexOf(searchQuery, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TrackAttributeHistory(GameplayAttribute attr)
        {
            string key = attr.Name;
            if (!attributeHistories.ContainsKey(key))
                attributeHistories[key] = new List<AttributeHistory>(MaxAttributeHistoryPerAttribute);

            var history = attributeHistories[key];
            var record = new AttributeHistory
            {
                Value = attr.CurrentValue,
                BaseValue = attr.BaseValue,
                Timestamp = EditorApplication.timeSinceStartup
            };

            history.Add(record);
            
            // Keep history bounded
            if (history.Count > MaxAttributeHistoryPerAttribute)
                history.RemoveAt(0);
        }

        private void ClearCaches()
        {
            effectNameCache.Clear();
            attributeRowWidthCache.Clear();
        }

        private void LoadUIState()
        {
            // Load persisted UI state from EditorPrefs
            // TODO: Implement EditorPrefs loading for fold states
        }

        private void SaveUIState()
        {
            // Save UI state to EditorPrefs for next session
            // TODO: Implement EditorPrefs saving for fold states
        }

        #endregion
    }
}
