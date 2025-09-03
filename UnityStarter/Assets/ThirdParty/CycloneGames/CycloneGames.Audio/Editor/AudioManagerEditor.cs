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
        
        // A reusable list to avoid GC allocation from LINQ's OrderBy.
        private readonly List<KeyValuePair<AudioClip, long>> sortedClipList = new List<KeyValuePair<AudioClip, long>>();

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

            DrawPoolStatus();
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
                foreach (var activeEvent in AudioManager.ActiveEvents)
                {
                    if (activeEvent == null || activeEvent.rootEvent == null) continue;
                    
                    string emitterName = activeEvent.emitterTransform != null ? activeEvent.emitterTransform.name : "No Emitter";
                    EditorGUILayout.LabelField($"{activeEvent.rootEvent.name} on {emitterName}");
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
