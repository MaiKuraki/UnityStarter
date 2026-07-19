using System;
using CycloneGames.GameplayFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    internal sealed class GameplayWorldDebuggerWindow : EditorWindow
    {
        private const double RefreshIntervalSeconds = 0.2d;

        private GameplayWorldHost host;
        private bool autoBind = true;
        private string actorFilter = string.Empty;
        private Vector2 scrollPosition;
        private double nextRefreshTime;

        [MenuItem("Tools/CycloneGames/GameplayFramework/World Debugger")]
        private static void OpenWindow()
        {
            GetWindow<GameplayWorldDebuggerWindow>("World Debugger");
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            TryBindHost();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < nextRefreshTime)
            {
                return;
            }

            nextRefreshTime = EditorApplication.timeSinceStartup + RefreshIntervalSeconds;
            if (autoBind && host == null)
            {
                TryBindHost();
            }

            if (Application.isPlaying || host != null)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            InspectorUiUtility.DrawSectionHeader(
                "World Debugger",
                "Observes one GameplayWorldHost and its active World. Actor registrations are read by dense index without creating a runtime collection snapshot.",
                new Color(0.42f, 0.78f, 1f, 1f));

            host = (GameplayWorldHost)EditorGUILayout.ObjectField(
                "World Host",
                host,
                typeof(GameplayWorldHost),
                true);
            autoBind = EditorGUILayout.ToggleLeft("Auto bind first loaded host", autoBind);

            if (GUILayout.Button("Find Loaded Host"))
            {
                TryBindHost();
            }

            if (host == null)
            {
                EditorGUILayout.HelpBox("No GameplayWorldHost is bound.", MessageType.Info);
                return;
            }

            DrawHostSummary(host);
            World world = host.CurrentWorld;
            if (world == null)
            {
                EditorGUILayout.HelpBox(
                    Application.isPlaying
                        ? "The host has no active World."
                        : "Enter Play Mode to inspect a running World.",
                    MessageType.Info);
                return;
            }

            DrawWorldSummary(world);
            DrawActorRegistrations(world);
        }

        private static void DrawHostSummary(GameplayWorldHost targetHost)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Host", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.EnumPopup("State", targetHost.State);
                EditorGUILayout.EnumPopup("Net Mode", targetHost.NetMode);
                EditorGUILayout.IntField("Local Players", targetHost.EffectiveLocalPlayerCount);
                EditorGUILayout.ObjectField(
                    "WorldSettings",
                    targetHost.WorldSettings,
                    typeof(WorldSettings),
                    false);
            }

            if (!string.IsNullOrEmpty(targetHost.LastError))
            {
                EditorGUILayout.HelpBox(targetHost.LastError, MessageType.Error);
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawWorldSummary(World world)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("World", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.EnumPopup("Lifecycle", world.LifecycleState);
                EditorGUILayout.Toggle("Authority", world.IsAuthority);
                EditorGUILayout.IntField("Actors", world.ActorCount);
                EditorGUILayout.IntField("World-Owned Actors", world.OwnedActorCount);
                EditorGUILayout.IntField(
                    "Update Tick Actors",
                    world.GetTickActorCount(ActorTickPhase.Update));
                EditorGUILayout.IntField(
                    "FixedUpdate Tick Actors",
                    world.GetTickActorCount(ActorTickPhase.FixedUpdate));
                EditorGUILayout.IntField(
                    "LateUpdate Tick Actors",
                    world.GetTickActorCount(ActorTickPhase.LateUpdate));
                EditorGUILayout.Toggle("Dispatching Tick", world.IsDispatchingActorTick);
                EditorGUILayout.EnumPopup("Active Tick Phase", world.ActiveTickPhase);
                EditorGUILayout.IntField("Player Controllers", world.PlayerControllers.Count);
                EditorGUILayout.IntField("Player Starts", world.PlayerStarts.Count);
                EditorGUILayout.ObjectField("Game Mode", world.GameMode, typeof(GameMode), true);
                EditorGUILayout.ObjectField("Game State", world.GameState, typeof(GameState), true);

                IGameSession session = world.GameMode?.GetGameSession();
                if (session != null)
                {
                    EditorGUILayout.IntField("Session Players", session.PlayerCount);
                    EditorGUILayout.IntField("Session Spectators", session.SpectatorCount);
                    EditorGUILayout.IntField("Max Players", session.MaxPlayers);
                    EditorGUILayout.IntField("Max Spectators", session.MaxSpectators);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawActorRegistrations(World world)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Actor Registrations", EditorStyles.boldLabel);
            actorFilter = EditorGUILayout.TextField("Filter", actorFilter ?? string.Empty);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            int visibleCount = 0;
            for (int i = 0; i < world.ActorCount; i++)
            {
                if (!world.TryGetActorRegistration(i, out WorldActorRegistration registration))
                {
                    continue;
                }

                Actor actor = registration.Actor;
                if (!MatchesFilter(actor))
                {
                    continue;
                }

                visibleCount++;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    $"[{i}] {actor.name}",
                    EditorStyles.boldLabel);
                if (GUILayout.Button("Select", GUILayout.Width(60f)))
                {
                    Selection.activeObject = actor.gameObject;
                    EditorGUIUtility.PingObject(actor.gameObject);
                }
                EditorGUILayout.EndHorizontal();

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Type", actor.GetType().FullName);
                    EditorGUILayout.EnumPopup("Lifecycle", actor.LifecycleState);
                    EditorGUILayout.Toggle("World Owned", registration.IsWorldOwned);
                    EditorGUILayout.Toggle("Deferred", registration.IsDeferred);
                    EditorGUILayout.Toggle("Can Ever Tick", actor.CanEverTick);
                    EditorGUILayout.EnumPopup("Tick Phase", actor.TickPhase);
                    EditorGUILayout.Toggle("Tick Enabled", actor.IsActorTickEnabled());
                    EditorGUILayout.ObjectField("Owner", actor.GetOwner(), typeof(Actor), true);
                    EditorGUILayout.ObjectField("Instigator", actor.GetInstigator(), typeof(Actor), true);
                }
                EditorGUILayout.EndVertical();
            }

            if (visibleCount == 0)
            {
                EditorGUILayout.HelpBox("No actor registrations match the filter.", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private bool MatchesFilter(Actor actor)
        {
            if (actor == null || string.IsNullOrWhiteSpace(actorFilter))
            {
                return actor != null;
            }

            return actor.name.IndexOf(actorFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   actor.GetType().Name.IndexOf(actorFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TryBindHost()
        {
            GameplayWorldHost[] hosts = UnityEngine.Object.FindObjectsByType<GameplayWorldHost>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            host = hosts.Length > 0 ? hosts[0] : null;
        }
    }
}
