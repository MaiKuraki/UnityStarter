using System;
using System.Collections.Generic;
using CycloneGames.GameplayFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    internal enum GameplayFrameworkValidationSeverity : byte
    {
        Info = 0,
        Warning = 1,
        Error = 2,
    }

    internal readonly struct GameplayFrameworkValidationIssue
    {
        public GameplayFrameworkValidationIssue(
            GameplayFrameworkValidationSeverity severity,
            string message,
            UnityEngine.Object context,
            string assetPath = null)
        {
            Severity = severity;
            Message = message;
            Context = context;
            AssetPath = assetPath;
        }

        public GameplayFrameworkValidationSeverity Severity { get; }
        public string Message { get; }
        public UnityEngine.Object Context { get; }
        public string AssetPath { get; }
    }

    internal static class GameplayFrameworkProjectValidator
    {
        public static void CollectProjectIssues(List<GameplayFrameworkValidationIssue> issues)
        {
            if (issues == null)
            {
                throw new ArgumentNullException(nameof(issues));
            }

            issues.Clear();
            string[] worldSettingsGuids = AssetDatabase.FindAssets("t:WorldSettings");
            Array.Sort(worldSettingsGuids, StringComparer.Ordinal);
            for (int i = 0; i < worldSettingsGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(worldSettingsGuids[i]);
                WorldSettings settings = AssetDatabase.LoadAssetAtPath<WorldSettings>(path);
                if (settings == null)
                {
                    issues.Add(new GameplayFrameworkValidationIssue(
                        GameplayFrameworkValidationSeverity.Error,
                        "A WorldSettings asset could not be loaded.",
                        null,
                        path));
                    continue;
                }

                ValidateWorldSettings(settings, issues, path);
            }

            GameplayWorldHost[] hosts = UnityEngine.Object.FindObjectsByType<GameplayWorldHost>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            ValidateHosts(hosts, issues);
        }

        internal static void ValidateWorldSettings(
            WorldSettings settings,
            List<GameplayFrameworkValidationIssue> issues,
            string assetPath = null)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (issues == null)
            {
                throw new ArgumentNullException(nameof(issues));
            }

            ValidateReference(
                "GameMode",
                settings.GameModeSource,
                settings.GameModeClass,
                settings.GameModeAssetLocation,
                required: true,
                settings,
                assetPath,
                issues);
            ValidateReference(
                "PlayerController",
                settings.PlayerControllerSource,
                settings.PlayerControllerClass,
                settings.PlayerControllerAssetLocation,
                required: true,
                settings,
                assetPath,
                issues);
            ValidateReference(
                "Pawn",
                settings.PawnSource,
                settings.PawnClass,
                settings.PawnAssetLocation,
                required: true,
                settings,
                assetPath,
                issues);
            ValidateReference(
                "PlayerState",
                settings.PlayerStateSource,
                settings.PlayerStateClass,
                settings.PlayerStateAssetLocation,
                required: true,
                settings,
                assetPath,
                issues);
            ValidateReference(
                "CameraManager",
                settings.CameraManagerSource,
                settings.CameraManagerClass,
                settings.CameraManagerAssetLocation,
                required: false,
                settings,
                assetPath,
                issues);
            ValidateReference(
                "SpectatorPawn",
                settings.SpectatorPawnSource,
                settings.SpectatorPawnClass,
                settings.SpectatorPawnAssetLocation,
                required: false,
                settings,
                assetPath,
                issues);
        }

        internal static void ValidateHosts(
            GameplayWorldHost[] hosts,
            List<GameplayFrameworkValidationIssue> issues)
        {
            int enabledAutoStartCount = 0;
            for (int i = 0; i < hosts.Length; i++)
            {
                GameplayWorldHost host = hosts[i];
                if (host != null && host.isActiveAndEnabled && host.AutoStart)
                {
                    enabledAutoStartCount++;
                }
            }

            for (int i = 0; i < hosts.Length; i++)
            {
                GameplayWorldHost host = hosts[i];
                if (host == null)
                {
                    continue;
                }

                if (host.WorldSettings == null)
                {
                    issues.Add(new GameplayFrameworkValidationIssue(
                        GameplayFrameworkValidationSeverity.Error,
                        "GameplayWorldHost requires WorldSettings.",
                        host));
                    continue;
                }

                if (host.WorldSettings.UsesExternalReferences && host.GetType() == typeof(GameplayWorldHost))
                {
                    issues.Add(new GameplayFrameworkValidationIssue(
                        GameplayFrameworkValidationSeverity.Error,
                        "GameplayWorldHost cannot resolve external WorldSettings locations without a project-specific resolver override.",
                        host));
                }

                if (enabledAutoStartCount > 1 && host.isActiveAndEnabled && host.AutoStart)
                {
                    issues.Add(new GameplayFrameworkValidationIssue(
                        GameplayFrameworkValidationSeverity.Error,
                        "Multiple enabled GameplayWorldHost components will auto-start in the loaded scenes. Scene Actor ownership would be ambiguous.",
                        host));
                }
            }
        }

        private static void ValidateReference<T>(
            string label,
            WorldSettingsReferenceSource source,
            T directReference,
            string location,
            bool required,
            WorldSettings settings,
            string assetPath,
            List<GameplayFrameworkValidationIssue> issues) where T : Component
        {
            if (source != WorldSettingsReferenceSource.DirectReference)
            {
                if (required && string.IsNullOrWhiteSpace(location))
                {
                    AddMissingIssue(label, source, settings, assetPath, issues);
                }

                return;
            }

            if (directReference == null)
            {
                if (required)
                {
                    AddMissingIssue(label, source, settings, assetPath, issues);
                }

                return;
            }

            if (!PrefabUtility.IsPartOfPrefabAsset(directReference))
            {
                issues.Add(new GameplayFrameworkValidationIssue(
                    GameplayFrameworkValidationSeverity.Error,
                    $"{label} direct reference must point to a prefab asset.",
                    settings,
                    assetPath));
                return;
            }

            T[] components = directReference.gameObject.GetComponents<T>();
            if (components.Length != 1)
            {
                issues.Add(new GameplayFrameworkValidationIssue(
                    GameplayFrameworkValidationSeverity.Error,
                    $"{label} prefab must contain exactly one {typeof(T).Name} component on its root, but found {components.Length}.",
                    settings,
                    assetPath));
            }
        }

        private static void AddMissingIssue(
            string label,
            WorldSettingsReferenceSource source,
            WorldSettings settings,
            string assetPath,
            List<GameplayFrameworkValidationIssue> issues)
        {
            issues.Add(new GameplayFrameworkValidationIssue(
                GameplayFrameworkValidationSeverity.Error,
                $"Required {label} reference is not configured for source '{source}'.",
                settings,
                assetPath));
        }
    }

    internal sealed class GameplayFrameworkValidationWindow : EditorWindow
    {
        private readonly List<GameplayFrameworkValidationIssue> issues =
            new List<GameplayFrameworkValidationIssue>(32);
        private Vector2 scrollPosition;

        [MenuItem("Tools/CycloneGames/GameplayFramework/Project Validation")]
        private static void OpenWindow()
        {
            GameplayFrameworkValidationWindow window =
                GetWindow<GameplayFrameworkValidationWindow>("Gameplay Validation");
            window.RunValidation();
        }

        private void OnEnable()
        {
            RunValidation();
        }

        private void OnGUI()
        {
            InspectorUiUtility.DrawSectionHeader(
                "Project Validation",
                "Scans every WorldSettings asset and GameplayWorldHost in currently loaded scenes. The scan is read-only and does not open or rewrite scenes.",
                new Color(0.42f, 0.78f, 1f, 1f));

            if (GUILayout.Button("Run Validation"))
            {
                RunValidation();
            }

            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No GameplayFramework configuration issues were found.", MessageType.Info);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            for (int i = 0; i < issues.Count; i++)
            {
                GameplayFrameworkValidationIssue issue = issues[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.HelpBox(issue.Message, ToMessageType(issue.Severity));
                if (!string.IsNullOrEmpty(issue.AssetPath))
                {
                    EditorGUILayout.SelectableLabel(
                        issue.AssetPath,
                        EditorStyles.miniLabel,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }

                if (issue.Context != null && GUILayout.Button("Select"))
                {
                    Selection.activeObject = issue.Context;
                    EditorGUIUtility.PingObject(issue.Context);
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        private void RunValidation()
        {
            GameplayFrameworkProjectValidator.CollectProjectIssues(issues);
            Repaint();
        }

        private static MessageType ToMessageType(GameplayFrameworkValidationSeverity severity)
        {
            switch (severity)
            {
                case GameplayFrameworkValidationSeverity.Error:
                    return MessageType.Error;
                case GameplayFrameworkValidationSeverity.Warning:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }
    }
}
