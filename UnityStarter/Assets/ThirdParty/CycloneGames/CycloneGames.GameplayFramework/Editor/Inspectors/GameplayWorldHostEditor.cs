using System;
using CycloneGames.GameplayFramework.Runtime;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    [CustomEditor(typeof(GameplayWorldHost), true)]
    [CanEditMultipleObjects]
    internal sealed class GameplayWorldHostEditor : UnityEditor.Editor
    {
        private static readonly string[] ManagedPropertyNames =
        {
            "worldSettings",
            "netMode",
            "autoStart",
            "localPlayerCount",
        };

        private SerializedProperty worldSettingsProperty;
        private SerializedProperty netModeProperty;
        private SerializedProperty autoStartProperty;
        private SerializedProperty localPlayerCountProperty;

        private void OnEnable()
        {
            worldSettingsProperty = serializedObject.FindProperty("worldSettings");
            netModeProperty = serializedObject.FindProperty("netMode");
            autoStartProperty = serializedObject.FindProperty("autoStart");
            localPlayerCountProperty = serializedObject.FindProperty("localPlayerCount");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            InspectorUiUtility.DrawSectionHeader(
                "World Composition",
                "This component owns one GameInstance and one active World. Use GameInstance directly when another composition root owns the lifecycle.",
                new Color(0.42f, 0.78f, 1f, 1f));

            EditorGUILayout.PropertyField(worldSettingsProperty);
            EditorGUILayout.PropertyField(netModeProperty);
            EditorGUILayout.PropertyField(autoStartProperty);

            bool dedicatedServer = !netModeProperty.hasMultipleDifferentValues &&
                                   (WorldNetMode)netModeProperty.intValue == WorldNetMode.DedicatedServer;
            using (new EditorGUI.DisabledScope(dedicatedServer))
            {
                EditorGUILayout.PropertyField(localPlayerCountProperty);
            }

            if (dedicatedServer)
            {
                EditorGUILayout.HelpBox(
                    "Dedicated Server mode always uses zero local players.",
                    MessageType.Info);
            }

            DrawConfigurationStatus();
            DrawRemainingProperties();
            serializedObject.ApplyModifiedProperties();

            if (!serializedObject.isEditingMultipleObjects)
            {
                DrawRuntimeControls((GameplayWorldHost)target);
            }
        }

        private void DrawConfigurationStatus()
        {
            if (worldSettingsProperty.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox(
                    "Selected hosts use different WorldSettings assets.",
                    MessageType.Info);
                return;
            }

            WorldSettings settings = worldSettingsProperty.objectReferenceValue as WorldSettings;
            if (settings == null)
            {
                EditorGUILayout.HelpBox("WorldSettings is required.", MessageType.Error);
                return;
            }

            if (!settings.Validate(false))
            {
                EditorGUILayout.HelpBox(
                    "WorldSettings is missing one or more required references. Open the asset to review its validation overview.",
                    MessageType.Error);
            }

            if (settings.UsesExternalReferences && target.GetType() == typeof(GameplayWorldHost))
            {
                EditorGUILayout.HelpBox(
                    "This WorldSettings uses external locations. Derive GameplayWorldHost and override CreateReferenceResolver, or compose GameInstance directly with a resolver.",
                    MessageType.Error);
            }
        }

        private void DrawRemainingProperties()
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.name == "m_Script" || Array.IndexOf(ManagedPropertyNames, iterator.name) >= 0)
                {
                    continue;
                }

                EditorGUILayout.PropertyField(iterator, true);
            }
        }

        private static void DrawRuntimeControls(GameplayWorldHost host)
        {
            EditorGUILayout.Space(8f);
            InspectorUiUtility.DrawSectionHeader(
                "Runtime State",
                "Controls operate only in Play Mode and do not modify serialized authoring data.",
                new Color(0.50f, 0.58f, 0.38f, 1f));

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.EnumPopup("Host State", host.State);
                EditorGUILayout.ObjectField("Game Mode", host.CurrentWorld?.GameMode, typeof(GameMode), true);
                EditorGUILayout.IntField("Effective Local Players", host.EffectiveLocalPlayerCount);
            }

            if (!string.IsNullOrEmpty(host.LastError))
            {
                EditorGUILayout.HelpBox(host.LastError, MessageType.Error);
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(
                           host.State == GameplayWorldHostState.Starting ||
                           host.State == GameplayWorldHostState.Stopping ||
                           host.IsRunning))
                {
                    if (GUILayout.Button("Start World"))
                    {
                        StartFromInspectorAsync(host).Forget();
                    }
                }

                using (new EditorGUI.DisabledScope(
                           host.State == GameplayWorldHostState.Starting ||
                           host.State == GameplayWorldHostState.Stopping ||
                           !host.IsRunning))
                {
                    if (GUILayout.Button("Stop World"))
                    {
                        StopFromInspectorAsync(host).Forget();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private static async UniTaskVoid StartFromInspectorAsync(GameplayWorldHost host)
        {
            try
            {
                await host.StartWorldAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, host);
            }
        }

        private static async UniTaskVoid StopFromInspectorAsync(GameplayWorldHost host)
        {
            try
            {
                await host.StopWorldAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, host);
            }
        }
    }
}
