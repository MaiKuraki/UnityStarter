// Copyright (c) CycloneGames
// Licensed under the MIT License.

using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using CycloneGames.Audio.Runtime;

namespace CycloneGames.Audio.Editor
{
    [CustomEditor(typeof(AudioManager), true)]
    public class AudioManagerEditor : UnityEditor.Editor
    {
        // Foldout states
        private bool showSettings = true;
        private bool showStatistics = true;
        private bool showPoolStatus = true;
        private bool showLoadedBanks = true;
        private bool showEventNameMap = false;
        private bool showMemoryUsage = false;
        private bool showExternalCache = true;
        private bool showActiveEvents = true;

        // Serialized properties for settings
        private SerializedProperty _focusModeProp;
        private SerializedProperty _customPoolSizeProp;
        private SerializedProperty _mainMixerProp;

        // Cached lists to avoid per-frame allocations
        private readonly List<KeyValuePair<AudioClip, long>> sortedClipList = new List<KeyValuePair<AudioClip, long>>();
        private readonly List<AudioBank> loadedBanksList = new List<AudioBank>();
        private readonly List<AudioBank> banksToUnload = new List<AudioBank>();
        private readonly List<ExternalAudioClipCacheEntryInfo> externalCacheEntries = new List<ExternalAudioClipCacheEntryInfo>();
        private readonly List<IAudioClipProvider> audioClipProviders = new List<IAudioClipProvider>();
        private readonly List<AudioCategoryVoiceStats> categoryVoiceStats = new List<AudioCategoryVoiceStats>();

        // Search field state
        private string searchEventName = "";

        // Scroll positions
        private Vector2 activeEventsScrollPos;
        private Vector2 memoryScrollPos;
        private Vector2 eventMapScrollPos;
        private Vector2 externalCacheScrollPos;

        // Colors for visual styling
        private static readonly Color settingsColor = new Color(0.5f, 0.5f, 0.6f);
        private static readonly Color statsColor = new Color(0.3f, 0.6f, 0.8f);
        private static readonly Color poolColor = new Color(0.4f, 0.7f, 0.4f);
        private static readonly Color banksColor = new Color(0.7f, 0.5f, 0.8f);
        private static readonly Color eventsColor = new Color(0.8f, 0.6f, 0.3f);
        private static readonly Color memoryColor = new Color(0.6f, 0.5f, 0.7f);
        private static readonly Color activeColor = new Color(0.5f, 0.7f, 0.6f);
        private static readonly Color cacheColor = new Color(0.4f, 0.65f, 0.75f);
        private static readonly Color successColor = new Color(0.3f, 0.8f, 0.4f);
        private static readonly Color warningColor = new Color(0.9f, 0.7f, 0.3f);

        // Cached GUIStyles to avoid per-frame allocations
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _statValueStyle;
        private GUIStyle _sectionLabelStyle;
        private bool _stylesInitialized;

        private void OnEnable()
        {
            _focusModeProp = serializedObject.FindProperty("focusMode");
            _customPoolSizeProp = serializedObject.FindProperty("customPoolSize");
            _mainMixerProp = serializedObject.FindProperty("mainMixer");
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };

            _subtitleStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 10
            };

            _statValueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18
            };

            _sectionLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11
            };
        }

        public override void OnInspectorGUI()
        {
            InitializeStyles();

            // Title
            DrawTitle();

            EditorGUILayout.Space(5);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Runtime data is only available in Play Mode.\nEnter Play Mode to see audio system status.", MessageType.Info);

                EditorGUILayout.Space(5);
                DrawDefaultInspector();
                return;
            }

            // Quick Stats Overview
            DrawQuickStats();

            EditorGUILayout.Space(3);

            // Settings Section (serialized fields)
            showSettings = InspectorUiUtility.DrawFoldoutHeader("Settings", showSettings, settingsColor);
            if (showSettings)
            {
                DrawSettingsSection();
            }

            EditorGUILayout.Space(3);

            // Statistics Section
            showStatistics = InspectorUiUtility.DrawFoldoutHeader("Overview", showStatistics, statsColor);
            if (showStatistics)
            {
                DrawStatisticsSection();
            }

            EditorGUILayout.Space(3);

            // Pool Status Section
            showPoolStatus = InspectorUiUtility.DrawFoldoutHeader("AudioSource Pool", showPoolStatus, poolColor);
            if (showPoolStatus)
            {
                DrawPoolStatusSection();
            }

            EditorGUILayout.Space(3);

            // Loaded Banks Section
            int bankCount = AudioManager.GetLoadedBankCount();
            showLoadedBanks = InspectorUiUtility.DrawFoldoutHeader($"Loaded Banks ({bankCount})", showLoadedBanks, banksColor);
            if (showLoadedBanks)
            {
                DrawLoadedBanksSection();
            }

            EditorGUILayout.Space(3);

            // Active Events Section
            int activeCount = AudioManager.ActiveEvents.Count;
            showActiveEvents = InspectorUiUtility.DrawFoldoutHeader($"Active Events ({activeCount})", showActiveEvents, activeColor);
            if (showActiveEvents)
            {
                DrawActiveEventsSection();
            }

            EditorGUILayout.Space(3);

            // Event Name Map Section
            int registeredCount = AudioManager.GetRegisteredEventCount();
            showEventNameMap = InspectorUiUtility.DrawFoldoutHeader($"Event Registry ({registeredCount})", showEventNameMap, eventsColor);
            if (showEventNameMap)
            {
                DrawEventNameMapSection();
            }

            EditorGUILayout.Space(3);

            // Memory Usage Section
            showMemoryUsage = InspectorUiUtility.DrawFoldoutHeader("Memory Usage", showMemoryUsage, memoryColor);
            if (showMemoryUsage)
            {
                DrawMemoryUsageSection();
            }

            EditorGUILayout.Space(3);

            ExternalAudioClipCacheStats externalStats = AudioClipResolver.GetExternalCacheStats();
            showExternalCache = InspectorUiUtility.DrawFoldoutHeader($"External Audio Cache ({externalStats.EntryCount})", showExternalCache, cacheColor);
            if (showExternalCache)
            {
                DrawExternalCacheSection(externalStats);
            }

            // Auto-repaint during play mode
            Repaint();
        }

        private void DrawTitle()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("Audio Manager", _titleStyle, GUILayout.Height(24));

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Runtime Audio System Controller", _subtitleStyle);
        }

        private void DrawSettingsSection()
        {
            serializedObject.Update();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(_focusModeProp);
            EditorGUILayout.PropertyField(_customPoolSizeProp, new GUIContent("Custom Pool Size"));
            EditorGUILayout.PropertyField(_mainMixerProp);

            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawQuickStats()
        {
            EditorGUILayout.BeginHorizontal();

            // Active Sources
            DrawStatBox("Active", AudioManager.PoolStats.InUse.ToString(), poolColor);

            // Loaded Banks
            DrawStatBox("Banks", AudioManager.GetLoadedBankCount().ToString(), banksColor);

            // Active Events
            DrawStatBox("Events", AudioManager.ActiveEvents.Count.ToString(), activeColor);

            // Memory
            DrawStatBox("Memory", ToMemorySizeString(AudioManager.TotalMemoryUsage), memoryColor);

            // External cache
            DrawStatBox("Ext Cache", AudioClipResolver.GetExternalCacheStats().EntryCount.ToString(), cacheColor);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatBox(string label, string value, Color color)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(70));

            EditorGUILayout.LabelField(label, EditorStyles.centeredGreyMiniLabel);

            _statValueStyle.normal.textColor = color;
            EditorGUILayout.LabelField(value, _statValueStyle, GUILayout.Height(22));

            EditorGUILayout.EndVertical();
        }

        private void DrawStatisticsSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DrawStatRow("Registered Events", AudioManager.GetRegisteredEventCount().ToString());
            DrawStatRow("Loaded Banks", AudioManager.GetLoadedBankCount().ToString());
            DrawStatRow("Active Events", AudioManager.ActiveEvents.Count.ToString());
            DrawStatRow("Total Memory", ToMemorySizeString(AudioManager.TotalMemoryUsage));

            ExternalAudioClipCacheStats cacheStats = AudioClipResolver.GetExternalCacheStats();
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("External Audio Cache", _sectionLabelStyle);
            DrawStatRow("Cached Entries", cacheStats.EntryCount.ToString());
            DrawStatRow("Loading", cacheStats.LoadingCount.ToString());
            DrawStatRow("Loaded", cacheStats.LoadedCount.ToString());
            DrawStatRow("Failed", cacheStats.FailedCount.ToString());
            DrawStatRow("Total Refs", cacheStats.TotalRefCount.ToString());
            DrawStatRow("Load Requests", cacheStats.TotalLoadRequests.ToString());
            DrawStatRow("Cache Hits", cacheStats.CacheHitCount.ToString());
            DrawStatRow("Cache Misses", cacheStats.CacheMissCount.ToString());
            DrawStatRow("Failures (Total)", cacheStats.TotalFailureCount.ToString());

            AudioManager.GetCategoryVoiceStats(categoryVoiceStats);
            if (categoryVoiceStats.Count > 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Category Voice Budgets", _sectionLabelStyle);
                for (int i = 0; i < categoryVoiceStats.Count; i++)
                {
                    AudioCategoryVoiceStats stat = categoryVoiceStats[i];
                    DrawStatRow(stat.Category.ToString(), $"{stat.ActiveSources}/{stat.Budget} (Weight {stat.WeightedLoad:0.##})");
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPoolStatusSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Config status
            string configStatus = AudioManager.PoolStats.HasConfig ? "[OK] Custom Config" : "[!] Using Defaults";
            Color statusColor = AudioManager.PoolStats.HasConfig ? successColor : warningColor;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Configuration", GUILayout.Width(100));
            GUI.color = statusColor;
            EditorGUILayout.LabelField(configStatus, EditorStyles.boldLabel);
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();

            DrawStatRow("Device Tier", AudioManager.PoolStats.DeviceTier);

            EditorGUILayout.Space(5);

            // Pool sizes grid
            EditorGUILayout.LabelField("Pool Status", _sectionLabelStyle);

            EditorGUILayout.BeginHorizontal();
            DrawMiniStatBox("Initial", AudioManager.PoolStats.InitialSize.ToString());
            DrawMiniStatBox("Current", AudioManager.PoolStats.CurrentSize.ToString());
            DrawMiniStatBox("Max", AudioManager.PoolStats.MaxSize.ToString());
            DrawMiniStatBox("Available", AudioManager.PoolStats.Available.ToString());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3);

            // Usage bar
            int activeSources = AudioManager.PoolStats.InUse;
            int totalSources = AudioManager.PoolStats.CurrentSize;
            float ratio = AudioManager.PoolStats.UsageRatio;

            Rect progressRect = EditorGUILayout.GetControlRect(false, 18);
            EditorGUI.ProgressBar(progressRect, ratio, $"Usage: {activeSources} / {totalSources} ({ratio:P0})");

            EditorGUILayout.Space(5);

            // Smart pool stats
            EditorGUILayout.LabelField("Performance", _sectionLabelStyle);

            EditorGUILayout.BeginHorizontal();
            DrawMiniStatBox("Peak", AudioManager.PoolStats.PeakUsage.ToString());
            DrawMiniStatBox("Expansions", AudioManager.PoolStats.TotalExpansions.ToString());
            DrawMiniStatBox("Steals", AudioManager.PoolStats.TotalSteals.ToString());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawMiniStatBox(string label, string value)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(60));
            EditorGUILayout.LabelField(label, EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawLoadedBanksSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            loadedBanksList.Clear();
            var banks = AudioManager.GetLoadedBanks();
            foreach (var bank in banks)
            {
                if (bank != null)
                {
                    loadedBanksList.Add(bank);
                }
            }

            if (loadedBanksList.Count == 0)
            {
                EditorGUILayout.LabelField("No banks loaded", EditorStyles.centeredGreyMiniLabel);
                EditorGUILayout.HelpBox("Use AudioManager.LoadBank() to load banks at runtime.", MessageType.Info);
            }
            else
            {
                for (int i = 0; i < loadedBanksList.Count; i++)
                {
                    var bank = loadedBanksList[i];
                    DrawBankRow(bank, i);
                }

                EditorGUILayout.Space(5);

                if (GUILayout.Button("Unload All Banks", GUILayout.Height(22)))
                {
                    if (EditorUtility.DisplayDialog("Unload All Banks",
                        "Are you sure you want to unload all banks?\nThis will remove all registered events.",
                        "Yes", "Cancel"))
                    {
                        banksToUnload.Clear();
                        banksToUnload.AddRange(loadedBanksList);
                        foreach (var bank in banksToUnload)
                        {
                            AudioManager.UnloadBank(bank);
                        }
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawBankRow(AudioBank bank, int index)
        {
            // Alternating background
            Rect rowRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (index % 2 == 0)
            {
                EditorGUI.DrawRect(rowRect, new Color(0.5f, 0.5f, 0.5f, 0.05f));
            }

            EditorGUILayout.BeginHorizontal();

            // Bank reference
            EditorGUILayout.ObjectField(bank, typeof(AudioBank), false, GUILayout.Width(150));

            // Event count
            int eventCount = bank.AudioEvents != null ? bank.AudioEvents.Count : 0;
            EditorGUILayout.LabelField($"{eventCount} events", EditorStyles.miniLabel, GUILayout.Width(60));

            // Duplicate warning
            var duplicates = AudioManager.ValidateBankForDuplicateNames(bank);
            if (duplicates.Count > 0)
            {
                GUI.color = warningColor;
                if (GUILayout.Button($"[!] {duplicates.Count} duplicates", EditorStyles.miniButton, GUILayout.Width(90)))
                {
                    string message = $"AudioBank '{bank.name}' has duplicate event names:\n\n";
                    foreach (var dup in duplicates)
                    {
                        message += $"'{dup.Key}': {dup.Value.Count} events\n";
                    }
                    message += "\nOnly the first event will be accessible via PlayEvent(string).";
                    EditorUtility.DisplayDialog("Duplicate Event Names", message, "OK");
                }
                GUI.color = Color.white;
            }

            GUILayout.FlexibleSpace();

            // Unload button (keeps playing events)
            if (GUILayout.Button("Unload", EditorStyles.miniButtonLeft, GUILayout.Width(50)))
            {
                if (EditorUtility.DisplayDialog("Unload Bank",
                    $"Unload '{bank.name}'?\n\nNote: Currently playing audio will continue.",
                    "Unload", "Cancel"))
                {
                    AudioManager.UnloadBank(bank);
                }
            }
            // Unload & Stop button (stops all events from this bank)
            if (GUILayout.Button("& Stop", EditorStyles.miniButtonRight, GUILayout.Width(45)))
            {
                if (EditorUtility.DisplayDialog("Unload & Stop",
                    $"Unload '{bank.name}' AND stop all playing events from this bank?",
                    "Yes", "Cancel"))
                {
                    AudioManager.UnloadBank(bank);
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawActiveEventsSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var activeEvents = AudioManager.ActiveEvents;

            if (activeEvents.Count == 0)
            {
                EditorGUILayout.LabelField("No active events", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                // Header
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Event", EditorStyles.miniLabel, GUILayout.Width(140));
                EditorGUILayout.LabelField("Category", EditorStyles.miniLabel, GUILayout.Width(80));
                EditorGUILayout.LabelField("Emitter", EditorStyles.miniLabel, GUILayout.Width(100));
                EditorGUILayout.LabelField("Sources", EditorStyles.miniLabel, GUILayout.Width(50));
                EditorGUILayout.LabelField("Status", EditorStyles.miniLabel, GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();

                // Separator
                Rect separatorRect = EditorGUILayout.GetControlRect(false, 1);
                EditorGUI.DrawRect(separatorRect, Color.gray * 0.5f);

                // Scrollable list (max 6 visible)
                float itemHeight = 20f;
                int visibleItems = Mathf.Min(6, activeEvents.Count);

                activeEventsScrollPos = EditorGUILayout.BeginScrollView(activeEventsScrollPos,
                    GUILayout.Height(visibleItems * itemHeight + 5));

                for (int i = 0; i < activeEvents.Count; i++)
                {
                    var activeEvent = activeEvents[i];
                    if (activeEvent == null || activeEvent.rootEvent == null) continue;

                    DrawActiveEventRow(activeEvent, i);
                }

                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawActiveEventRow(ActiveEvent activeEvent, int index)
        {
            Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(18));
            if (index % 2 == 0)
            {
                EditorGUI.DrawRect(rowRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));
            }

            // Event name
            EditorGUILayout.LabelField(activeEvent.rootEvent.name, GUILayout.Width(140));

            // Category
            EditorGUILayout.LabelField(activeEvent.rootEvent.Category.ToString(), EditorStyles.miniLabel, GUILayout.Width(80));

            // Emitter name
            string emitterName = activeEvent.emitterTransform != null ? activeEvent.emitterTransform.name : "-";
            EditorGUILayout.LabelField(emitterName, EditorStyles.miniLabel, GUILayout.Width(100));

            // Source count
            EditorGUILayout.LabelField(activeEvent.SourceCount.ToString(), EditorStyles.miniLabel, GUILayout.Width(50));

            // Status with color
            string status = activeEvent.status.ToString();
            Color statusColor = activeEvent.status == EventStatus.Played ? successColor : Color.gray;
            GUI.color = statusColor;
            EditorGUILayout.LabelField(status, EditorStyles.miniLabel, GUILayout.Width(80));
            GUI.color = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawEventNameMapSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            int registeredCount = AudioManager.GetRegisteredEventCount();

            // Search bar
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search:", GUILayout.Width(50));
            searchEventName = EditorGUILayout.TextField(searchEventName, GUILayout.MinWidth(120));

            if (GUILayout.Button("Find", EditorStyles.miniButtonLeft, GUILayout.Width(40)))
            {
                if (!string.IsNullOrEmpty(searchEventName))
                {
                    var foundEvent = AudioManager.GetEventByName(searchEventName);
                    if (foundEvent != null)
                    {
                        EditorUtility.FocusProjectWindow();
                        Selection.activeObject = foundEvent;
                        EditorGUIUtility.PingObject(foundEvent);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Event Not Found",
                            $"Event '{searchEventName}' is not registered.\nMake sure the bank is loaded.",
                            "OK");
                    }
                }
            }
            if (GUILayout.Button("Clear", EditorStyles.miniButtonRight, GUILayout.Width(40)))
            {
                searchEventName = "";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3);

            // Event list
            if (registeredCount > 0 && registeredCount <= 50)
            {
                eventMapScrollPos = EditorGUILayout.BeginScrollView(eventMapScrollPos, GUILayout.Height(120));

                var allBanks = AudioManager.GetLoadedBanks();
                foreach (var bank in allBanks)
                {
                    if (bank == null || bank.AudioEvents == null) continue;
                    foreach (var evt in bank.AudioEvents)
                    {
                        if (evt != null && !string.IsNullOrEmpty(evt.name))
                        {
                            EditorGUILayout.BeginHorizontal();
                            if (GUILayout.Button(evt.name, EditorStyles.linkLabel, GUILayout.Width(180)))
                            {
                                EditorUtility.FocusProjectWindow();
                                Selection.activeObject = evt;
                                EditorGUIUtility.PingObject(evt);
                            }
                            EditorGUILayout.LabelField($"[{bank.name}]", EditorStyles.miniLabel);
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                }

                EditorGUILayout.EndScrollView();
            }
            else if (registeredCount > 50)
            {
                EditorGUILayout.HelpBox($"Too many events ({registeredCount}) to display.\nUse search to find specific events.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField("No events registered", EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawMemoryUsageSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField($"Total Tracked: {ToMemorySizeString(AudioManager.TotalMemoryUsage)}", EditorStyles.boldLabel);

            EditorGUILayout.Space(3);

            // Populate and sort the list
            sortedClipList.Clear();
            foreach (var kvp in AudioManager.ClipMemoryCache)
            {
                sortedClipList.Add(kvp);
            }
            sortedClipList.Sort((a, b) => b.Value.CompareTo(a.Value));

            if (sortedClipList.Count == 0)
            {
                EditorGUILayout.LabelField("No audio clips loaded", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                memoryScrollPos = EditorGUILayout.BeginScrollView(memoryScrollPos, GUILayout.Height(100));

                for (int i = 0; i < sortedClipList.Count; i++)
                {
                    var kvp = sortedClipList[i];
                    if (kvp.Key == null) continue;

                    Rect rowRect = EditorGUILayout.BeginHorizontal();
                    if (i % 2 == 0)
                    {
                        EditorGUI.DrawRect(rowRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));
                    }

                    int refCount = AudioManager.ActiveClipRefCount.TryGetValue(kvp.Key, out int count) ? count : 0;

                    EditorGUILayout.LabelField(kvp.Key.name, GUILayout.MinWidth(120));
                    EditorGUILayout.LabelField($"{refCount} refs", EditorStyles.miniLabel, GUILayout.Width(45));
                    EditorGUILayout.LabelField(ToMemorySizeString(kvp.Value), EditorStyles.boldLabel, GUILayout.Width(70));

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.HelpBox("Tracks raw AudioClip sample data. Does not include Unity audio engine overhead.", MessageType.None);

            EditorGUILayout.EndVertical();
        }

        private void DrawExternalCacheSection(ExternalAudioClipCacheStats stats)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            DrawMiniStatBox("Entries", stats.EntryCount.ToString());
            DrawMiniStatBox("Loading", stats.LoadingCount.ToString());
            DrawMiniStatBox("Loaded", stats.LoadedCount.ToString());
            DrawMiniStatBox("Failed", stats.FailedCount.ToString());
            DrawMiniStatBox("Refs", stats.TotalRefCount.ToString());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawMiniStatBox("Requests", stats.TotalLoadRequests.ToString());
            DrawMiniStatBox("Hits", stats.CacheHitCount.ToString());
            DrawMiniStatBox("Misses", stats.CacheMissCount.ToString());
            DrawMiniStatBox("Failures", stats.TotalFailureCount.ToString());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            AudioClipResolver.GetProviders(audioClipProviders);
            if (audioClipProviders.Count > 0)
            {
                EditorGUILayout.LabelField("Providers", _sectionLabelStyle);
                for (int i = 0; i < audioClipProviders.Count; i++)
                {
                    IAudioClipProvider provider = audioClipProviders[i];
                    if (provider == null) continue;
                    EditorGUILayout.LabelField($"[{provider.Priority}] {provider.Name}", EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(4);
            }

            AudioClipResolver.GetExternalCacheEntries(externalCacheEntries);
            externalCacheEntries.Sort((a, b) => string.Compare(a.Location, b.Location, System.StringComparison.Ordinal));

            if (externalCacheEntries.Count == 0)
            {
                EditorGUILayout.LabelField("No external audio clips cached", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Location", EditorStyles.miniLabel, GUILayout.Width(260));
                EditorGUILayout.LabelField("State", EditorStyles.miniLabel, GUILayout.Width(55));
                EditorGUILayout.LabelField("Refs", EditorStyles.miniLabel, GUILayout.Width(35));
                EditorGUILayout.LabelField("Clip", EditorStyles.miniLabel, GUILayout.Width(90));
                EditorGUILayout.EndHorizontal();

                Rect separatorRect = EditorGUILayout.GetControlRect(false, 1);
                EditorGUI.DrawRect(separatorRect, Color.gray * 0.5f);

                externalCacheScrollPos = EditorGUILayout.BeginScrollView(externalCacheScrollPos, GUILayout.Height(140));

                for (int i = 0; i < externalCacheEntries.Count; i++)
                {
                    DrawExternalCacheRow(externalCacheEntries[i], i);
                }

                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.HelpBox("Shows shared external clip cache entries used by AudioClipReference and legacy file-path loading.", MessageType.None);

            EditorGUILayout.EndVertical();
        }

        private void DrawExternalCacheRow(ExternalAudioClipCacheEntryInfo entry, int index)
        {
            Rect rowRect = EditorGUILayout.BeginHorizontal();
            if (index % 2 == 0)
            {
                EditorGUI.DrawRect(rowRect, new Color(0.5f, 0.5f, 0.5f, 0.1f));
            }

            string state;
            Color stateColor;
            if (!entry.IsDone)
            {
                state = "Loading";
                stateColor = warningColor;
            }
            else if (entry.IsSuccess)
            {
                state = "Ready";
                stateColor = successColor;
            }
            else
            {
                state = "Failed";
                stateColor = new Color(0.9f, 0.4f, 0.4f);
            }

            EditorGUILayout.LabelField(entry.Location, EditorStyles.wordWrappedMiniLabel, GUILayout.Width(260));
            GUI.color = stateColor;
            EditorGUILayout.LabelField(state, EditorStyles.miniLabel, GUILayout.Width(55));
            GUI.color = Color.white;
            EditorGUILayout.LabelField(entry.RefCount.ToString(), EditorStyles.miniLabel, GUILayout.Width(35));
            EditorGUILayout.LabelField(string.IsNullOrEmpty(entry.ClipName) ? "-" : entry.ClipName, EditorStyles.miniLabel, GUILayout.Width(90));
            EditorGUILayout.EndHorizontal();

            if (entry.IsDone && !entry.IsSuccess && !string.IsNullOrEmpty(entry.Error))
            {
                EditorGUILayout.LabelField(entry.Error, EditorStyles.miniLabel);
            }
        }

        private void DrawStatRow(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(120));
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
        }

        #region Utility Methods

        private static string ToMemorySizeString(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
            return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
        }

        #endregion
    }
}
