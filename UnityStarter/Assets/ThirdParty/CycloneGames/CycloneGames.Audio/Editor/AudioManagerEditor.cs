using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using CycloneGames.Audio.Runtime;

namespace CycloneGames.Audio.Editor
{
    [CustomEditor(typeof(AudioManager))]
    public class AudioManagerEditor : UnityEditor.Editor
    {
        private bool showPoolStatus = true;
        private bool showActiveEvents = true;
        private bool showMemoryUsage = true;
        private bool showLoadedBanks = true;
        private bool showEventNameMap = true;
        private bool showStatistics = true;

        private readonly List<KeyValuePair<AudioClip, long>> sortedClipList = new List<KeyValuePair<AudioClip, long>>();
        private readonly List<AudioBank> loadedBanksList = new List<AudioBank>();

        // Search field state
        private string searchEventName = "";

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            Repaint();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("------ Audio Management State ------", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Runtime data is only available in Play Mode.", MessageType.Info);
                return;
            }

            DrawStatistics();
            DrawPoolStatus();
            DrawLoadedBanks();
            DrawEventNameMap();
            DrawMemoryUsage();
            DrawActiveEvents();
        }

        private void DrawPoolStatus()
        {
            showPoolStatus = EditorGUILayout.Foldout(showPoolStatus, "AudioSource Pool Status", true);
            if (showPoolStatus)
            {
                EditorGUI.indentLevel++;
                int totalSources = AudioManager.SourcePool.Count;
                int availableSources = AudioManager.AvailableSources.Count;
                int activeSources = totalSources - availableSources;

                EditorGUILayout.LabelField("Total Sources", totalSources.ToString());
                EditorGUILayout.LabelField("Active Sources", activeSources.ToString());
                EditorGUILayout.LabelField("Available Sources", availableSources.ToString());

                Rect progressRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                EditorGUI.ProgressBar(progressRect, (float)activeSources / totalSources, $"{activeSources} / {totalSources} Used");

                EditorGUI.indentLevel--;
            }
        }

        private void DrawMemoryUsage()
        {
            showMemoryUsage = EditorGUILayout.Foldout(showMemoryUsage, "Memory Usage", true);
            if (showMemoryUsage)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.HelpBox("This tracks the memory of raw AudioClip sample data managed by the AudioManager. It does not include the full overhead of the Unity audio engine (FMOD).", MessageType.Info);

                EditorGUILayout.LabelField("Tracked AudioClip Memory", ToMemorySizeString(AudioManager.TotalMemoryUsage));

                // Populate the reusable list and sort it to avoid LINQ overhead.
                sortedClipList.Clear();
                foreach (var kvp in AudioManager.ClipMemoryCache)
                {
                    sortedClipList.Add(kvp);
                }
                sortedClipList.Sort((a, b) => b.Value.CompareTo(a.Value));

                foreach (var kvp in sortedClipList)
                {
                    if (kvp.Key == null) continue;

                    int refCount = AudioManager.ActiveClipRefCount.TryGetValue(kvp.Key, out int count) ? count : 0;
                    EditorGUILayout.LabelField($"{kvp.Key.name} ({refCount} refs)", ToMemorySizeString(kvp.Value));
                }
                EditorGUI.indentLevel--;
            }
        }

        private void DrawActiveEvents()
        {
            showActiveEvents = EditorGUILayout.Foldout(showActiveEvents, $"Active Events ({AudioManager.ActiveEvents.Count})", true);
            if (showActiveEvents)
            {
                EditorGUI.indentLevel++;
                if (AudioManager.ActiveEvents.Count == 0)
                {
                    EditorGUILayout.HelpBox("No active events.", MessageType.Info);
                }
                else
                {
                    foreach (var activeEvent in AudioManager.ActiveEvents)
                    {
                        if (activeEvent == null || activeEvent.rootEvent == null) continue;

                        string emitterName = activeEvent.emitterTransform != null ? activeEvent.emitterTransform.name : "No Emitter";
                        string status = activeEvent.status.ToString();
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"{activeEvent.rootEvent.name}", GUILayout.Width(200));
                        EditorGUILayout.LabelField($"on {emitterName}", GUILayout.Width(150));
                        EditorGUILayout.LabelField($"Status: {status}", GUILayout.Width(100));
                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUI.indentLevel--;
            }
        }

        private void DrawStatistics()
        {
            showStatistics = EditorGUILayout.Foldout(showStatistics, "Statistics", true);
            if (showStatistics)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.LabelField("Registered Events", AudioManager.GetRegisteredEventCount().ToString());
                EditorGUILayout.LabelField("Loaded Banks", AudioManager.GetLoadedBankCount().ToString());
                EditorGUILayout.LabelField("Active Events", AudioManager.ActiveEvents.Count.ToString());
                EditorGUILayout.LabelField("Total Memory Usage", ToMemorySizeString(AudioManager.TotalMemoryUsage));

                EditorGUI.indentLevel--;
            }
        }

        private void DrawLoadedBanks()
        {
            showLoadedBanks = EditorGUILayout.Foldout(showLoadedBanks, $"Loaded Banks ({AudioManager.GetLoadedBankCount()})", true);
            if (showLoadedBanks)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.HelpBox("LoadBank only registers event names for lookup. Audio clips are loaded on-demand when events are played.\n\n" +
                    "Warning: Duplicate event names within the same bank will cause only the first one to be accessible via PlayEvent(string).", MessageType.Info);

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
                    EditorGUILayout.HelpBox("No banks loaded. Use AudioManager.LoadBank() to load banks.", MessageType.Info);
                }
                else
                {
                    foreach (var bank in loadedBanksList)
                    {
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.ObjectField(bank, typeof(AudioBank), false, GUILayout.Width(200));

                        if (bank.AudioEvents != null)
                        {
                            EditorGUILayout.LabelField($"({bank.AudioEvents.Count} events)", GUILayout.Width(100));
                            
                            // Check for duplicate names
                            var duplicates = AudioManager.ValidateBankForDuplicateNames(bank);
                            if (duplicates.Count > 0)
                            {
                                EditorGUILayout.LabelField("⚠", GUILayout.Width(20));
                                if (GUILayout.Button($"View {duplicates.Count} Duplicates", EditorStyles.miniButton, GUILayout.Width(120)))
                                {
                                    string message = $"AudioBank '{bank.name}' has duplicate event names:\n\n";
                                    foreach (var dup in duplicates)
                                    {
                                        message += $"'{dup.Key}': {dup.Value.Count} events\n";
                                    }
                                    message += "\nOnly the first event of each name will be accessible via PlayEvent(string).";
                                    EditorUtility.DisplayDialog("Duplicate Event Names", message, "OK");
                                }
                            }
                        }
                        EditorGUILayout.EndHorizontal();

                        EditorGUILayout.BeginHorizontal();
                        if (GUILayout.Button("Unload", GUILayout.Width(80)))
                        {
                            if (EditorUtility.DisplayDialog("Unload Bank",
                                $"Unload '{bank.name}'? This will remove event name mappings.\n\nNote: Currently playing audio from this bank will continue playing.",
                                "Unload", "Cancel"))
                            {
                                AudioManager.UnloadBank(bank);
                            }
                        }
                        if (GUILayout.Button("Unload & Stop", GUILayout.Width(100)))
                        {
                            if (EditorUtility.DisplayDialog("Unload Bank and Stop Events",
                                $"Unload '{bank.name}' and stop all active events from this bank?",
                                "Yes", "Cancel"))
                            {
                                AudioManager.UnloadBankAndStopEvents(bank);
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                    }
                }

                EditorGUILayout.Space();
                if (GUILayout.Button("Clear All Banks"))
                {
                    if (EditorUtility.DisplayDialog("Clear All Banks",
                        "Are you sure you want to unload all banks? This will remove all registered events.",
                        "Yes", "No"))
                    {
                        var banksToUnload = new List<AudioBank>(loadedBanksList);
                        foreach (var bank in banksToUnload)
                        {
                            AudioManager.UnloadBank(bank);
                        }
                    }
                }

                EditorGUI.indentLevel--;
            }
        }

        private void DrawEventNameMap()
        {
            showEventNameMap = EditorGUILayout.Foldout(showEventNameMap, $"Event Name Map ({AudioManager.GetRegisteredEventCount()})", true);
            if (showEventNameMap)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.HelpBox("This shows all events registered by name. Use AudioManager.PlayEvent(string) to play events by name.", MessageType.Info);

                // Show registered event count
                int registeredCount = AudioManager.GetRegisteredEventCount();
                EditorGUILayout.HelpBox($"Total registered events: {registeredCount}", MessageType.None);

                if (registeredCount > 0)
                {
                    EditorGUILayout.HelpBox("Use AudioManager.PlayEvent(string) to play events by name.", MessageType.Info);
                }

                // Show search functionality
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Search Event by Name:", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                searchEventName = EditorGUILayout.TextField(searchEventName, GUILayout.Width(200));
                if (GUILayout.Button("Find", GUILayout.Width(60)))
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
                                $"Event '{searchEventName}' is not registered. Make sure the bank containing this event is loaded.",
                                "OK");
                        }
                    }
                }
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                {
                    searchEventName = "";
                }
                EditorGUILayout.EndHorizontal();

                // Show all registered event names for reference
                if (registeredCount > 0 && registeredCount <= 50) // Only show if reasonable number
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Registered Event Names:", EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    var allBanks = AudioManager.GetLoadedBanks();
                    foreach (var bank in allBanks)
                    {
                        if (bank == null || bank.AudioEvents == null) continue;
                        foreach (var evt in bank.AudioEvents)
                        {
                            if (evt != null && !string.IsNullOrEmpty(evt.name))
                            {
                                EditorGUILayout.BeginHorizontal();
                                if (GUILayout.Button(evt.name, EditorStyles.linkLabel, GUILayout.Width(200)))
                                {
                                    EditorUtility.FocusProjectWindow();
                                    Selection.activeObject = evt;
                                    EditorGUIUtility.PingObject(evt);
                                }
                                EditorGUILayout.LabelField($"from {bank.name}", EditorStyles.miniLabel);
                                EditorGUILayout.EndHorizontal();
                            }
                        }
                    }
                    EditorGUILayout.EndVertical();
                    EditorGUI.indentLevel--;
                }
                else if (registeredCount > 50)
                {
                    EditorGUILayout.HelpBox($"Too many events ({registeredCount}) to display. Use search to find specific events.", MessageType.Info);
                }

                EditorGUI.indentLevel--;
            }
        }

        private static string ToMemorySizeString(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F2} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
            return $"{(bytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
        }
    }
}
