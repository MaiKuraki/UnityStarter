using UnityEditor;
using UnityEngine;
using System.Text;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Editor
{
    /// <summary>
    /// Runtime debugger window for inspecting AbilitySystemComponent state.
    /// Shows active effects, attributes, and tags in real-time.
    /// </summary>
    public class AbilitySystemDebuggerWindow : EditorWindow
    {
        private static readonly GUIContent s_ReusableContent = new GUIContent();
        private static readonly Color s_EffectColor = new Color(0.3f, 0.7f, 0.4f, 1f);
        private static readonly Color s_ExpiredColor = new Color(0.7f, 0.3f, 0.3f, 1f);
        private static readonly Color s_TagColor = new Color(0.4f, 0.6f, 0.9f, 1f);

        private GameObject selectedGameObject;
        private AbilitySystemComponent selectedASC;
        private Vector2 scrollPosition;
        private bool showEffects = true;
        private bool showAttributes = true;
        private bool showTags = true;
        private bool showPoolStats = false;
        private float refreshRate = 0.1f;
        private double lastRefreshTime;
        private readonly StringBuilder sb = new StringBuilder(256);

        [MenuItem("Tools/CycloneGames/GAS Debugger")]
        public static void ShowWindow()
        {
            var window = GetWindow<AbilitySystemDebuggerWindow>("GAS Debugger");
            window.minSize = new Vector2(350, 400);
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            selectedASC = null;
            Repaint();
        }

        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup - lastRefreshTime > refreshRate)
            {
                lastRefreshTime = EditorApplication.timeSinceStartup;
                Repaint();
            }
        }

        private void OnGUI()
        {
            DrawToolbar();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to debug AbilitySystemComponents.", MessageType.Info);
            }
            else if (selectedASC == null)
            {
                EditorGUILayout.HelpBox("Select a GameObject with an AbilitySystemComponent, or assign one below.", MessageType.Info);

                // Manual selection
                EditorGUI.BeginChangeCheck();
                selectedGameObject = EditorGUILayout.ObjectField("Target", selectedGameObject, typeof(GameObject), true) as GameObject;
                if (EditorGUI.EndChangeCheck() && selectedGameObject != null)
                {
                    TryFindASC();
                }
            }
            else
            {
                DrawASCDebugInfo();
            }

            if (showPoolStats)
            {
                DrawPoolStatistics();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                TryFindASC();
            }

            GUILayout.FlexibleSpace();

            refreshRate = EditorGUILayout.Slider(refreshRate, 0.05f, 1f, GUILayout.Width(120));
            EditorGUILayout.LabelField("Hz", GUILayout.Width(25));

            showPoolStats = GUILayout.Toggle(showPoolStats, "Pools", EditorStyles.toolbarButton);

            EditorGUILayout.EndHorizontal();
        }

        private void TryFindASC()
        {
            if (Selection.activeGameObject != null)
            {
                selectedGameObject = Selection.activeGameObject;
            }

            // For demo, we look for a component that might bridge to ASC
            // In production, you'd have a MonoBehaviour that exposes ASC
            selectedASC = null;
        }

        private void DrawASCDebugInfo()
        {
            EditorGUILayout.LabelField($"ASC: {selectedASC.OwnerActor}", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            DrawActiveEffectsSection();
            EditorGUILayout.Space(4);
            DrawTagsSection();
        }

        private void DrawActiveEffectsSection()
        {
            showEffects = EditorGUILayout.BeginFoldoutHeaderGroup(showEffects,
                $"Active Effects ({selectedASC.ActiveEffects?.Count ?? 0})");

            if (showEffects && selectedASC.ActiveEffects != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                foreach (var effect in selectedASC.ActiveEffects)
                {
                    DrawEffectEntry(effect);
                }

                if (selectedASC.ActiveEffects.Count == 0)
                {
                    EditorGUILayout.LabelField("No active effects", EditorStyles.centeredGreyMiniLabel);
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawEffectEntry(ActiveGameplayEffect effect)
        {
            if (effect?.Spec?.Def == null) return;

            var originalBg = GUI.backgroundColor;
            GUI.backgroundColor = effect.IsExpired ? s_ExpiredColor : s_EffectColor;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalBg;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(effect.Spec.Def.Name, EditorStyles.boldLabel);

            // Stack badge
            if (effect.StackCount > 1)
            {
                GUILayout.Label($"x{effect.StackCount}", EditorStyles.miniLabel, GUILayout.Width(30));
            }
            EditorGUILayout.EndHorizontal();

            // Duration bar
            if (effect.Spec.Def.DurationPolicy == EDurationPolicy.HasDuration)
            {
                float progress = effect.Spec.Duration > 0
                    ? effect.TimeRemaining / effect.Spec.Duration
                    : 0f;

                Rect rect = EditorGUILayout.GetControlRect(false, 4);
                EditorGUI.ProgressBar(rect, progress, "");
                EditorGUILayout.LabelField($"{effect.TimeRemaining:F1}s remaining", EditorStyles.miniLabel);
            }
            else if (effect.Spec.Def.DurationPolicy == EDurationPolicy.Infinite)
            {
                EditorGUILayout.LabelField("∞ Infinite", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTagsSection()
        {
            showTags = EditorGUILayout.BeginFoldoutHeaderGroup(showTags, "Combined Tags");

            if (showTags)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                var tags = selectedASC.CombinedTags;
                if (tags != null)
                {
                    EditorGUILayout.LabelField("Tag container active", EditorStyles.centeredGreyMiniLabel);
                    // In production, iterate and display tags
                }
                else
                {
                    EditorGUILayout.LabelField("No tags", EditorStyles.centeredGreyMiniLabel);
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawPoolStatistics()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Pool Statistics", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // GameplayEffectSpec pool stats
            var specStats = GASPool<GameplayEffectSpec>.Shared.GetStatistics();
            DrawPoolStatLine("EffectSpec", specStats);

            var activeStats = GASPool<ActiveGameplayEffect>.Shared.GetStatistics();
            DrawPoolStatLine("ActiveEffect", activeStats);

            var contextStats = GASPool<GameplayEffectContext>.Shared.GetStatistics();
            DrawPoolStatLine("Context", contextStats);

            var abilitySpecStats = GASPool<GameplayAbilitySpec>.Shared.GetStatistics();
            DrawPoolStatLine("AbilitySpec", abilitySpecStats);

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Warm All Pools (32)"))
            {
                GASPool<GameplayEffectSpec>.Shared.Warm(32);
                GASPool<ActiveGameplayEffect>.Shared.Warm(32);
                GASPool<GameplayEffectContext>.Shared.Warm(32);
                GASPool<GameplayAbilitySpec>.Shared.Warm(32);
            }

            if (GUILayout.Button("Aggressive Shrink All"))
            {
                GASPoolRegistry.AggressiveShrinkAll();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPoolStatLine(string name, GASPoolStatistics stats)
        {
            sb.Clear();
            sb.Append(name).Append(": ");
            sb.Append("Pool=").Append(stats.PoolSize);
            sb.Append(" Active=").Append(stats.ActiveCount);
            sb.Append(" Peak=").Append(stats.PeakActive);
            sb.Append(" Hit=").Append((stats.HitRate * 100).ToString("F0")).Append('%');

            EditorGUILayout.LabelField(sb.ToString(), EditorStyles.miniLabel);
        }
    }
}
