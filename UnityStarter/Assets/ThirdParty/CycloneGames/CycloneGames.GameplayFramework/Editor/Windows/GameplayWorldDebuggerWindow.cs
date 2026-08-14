using System;
using CycloneGames.GameplayFramework.Core;
using CycloneGames.GameplayFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    internal readonly struct ActorRegistrationPage
    {
        private ActorRegistrationPage(
            int pageIndex,
            int pageCount,
            int startIndex,
            int endIndexExclusive)
        {
            PageIndex = pageIndex;
            PageCount = pageCount;
            StartIndex = startIndex;
            EndIndexExclusive = endIndexExclusive;
        }

        public int PageIndex { get; }
        public int PageCount { get; }
        public int StartIndex { get; }
        public int EndIndexExclusive { get; }

        public static ActorRegistrationPage Create(
            int totalCount,
            int requestedPageIndex,
            int pageSize)
        {
            if (totalCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(totalCount));
            }

            if (pageSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            }

            int pageCount = totalCount == 0
                ? 1
                : ((totalCount - 1) / pageSize) + 1;
            int pageIndex = Math.Max(0, Math.Min(requestedPageIndex, pageCount - 1));
            int startIndex = pageIndex * pageSize;
            int endIndexExclusive = Math.Min(totalCount, startIndex + pageSize);
            return new ActorRegistrationPage(
                pageIndex,
                pageCount,
                startIndex,
                endIndexExclusive);
        }
    }

    internal sealed class GameplayWorldDebuggerWindow : EditorWindow
    {
        private const double RefreshIntervalSeconds = 0.2d;
        private const int ActorPageSize = 32;

        private GameplayWorldHost host;
        private bool autoBind = true;
        private int actorPageIndex;
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
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            if (autoBind)
            {
                TryBindHost();
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < nextRefreshTime)
            {
                return;
            }

            nextRefreshTime = EditorApplication.timeSinceStartup + RefreshIntervalSeconds;
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

            EditorGUI.BeginChangeCheck();
            GameplayWorldHost selectedHost = (GameplayWorldHost)EditorGUILayout.ObjectField(
                "World Host",
                host,
                typeof(GameplayWorldHost),
                true);
            if (EditorGUI.EndChangeCheck())
            {
                host = selectedHost;
                actorPageIndex = 0;
            }

            EditorGUI.BeginChangeCheck();
            bool nextAutoBind = EditorGUILayout.ToggleLeft("Auto bind first loaded host", autoBind);
            if (EditorGUI.EndChangeCheck())
            {
                autoBind = nextAutoBind;
                if (autoBind && host == null)
                {
                    TryBindHost();
                }
            }

            if (GUILayout.Button("Find Loaded Host"))
            {
                TryBindHost();
            }

            if (host == null)
            {
                EditorGUILayout.HelpBox("No GameplayWorldHost is bound.", MessageType.Info);
                return;
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode to inspect live Host and World state.",
                    MessageType.Info);
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
                EditorGUILayout.Toggle("Explicit Composition", targetHost.HasExplicitComposition);
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
            ActorAdmissionSnapshot admission = world.GetActorAdmissionSnapshot();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("World", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.EnumPopup("Lifecycle", world.LifecycleState);
                EditorGUILayout.Toggle("Authority", world.IsAuthority);
                EditorGUILayout.IntField("Actors", world.ActorCount);
                EditorGUILayout.IntField("World-Owned Actors", world.OwnedActorCount);
                EditorGUILayout.IntField("Peak Actors", admission.PeakActorCount);
                EditorGUILayout.IntField("Actor Admission Limit", admission.MaximumActorCount);
                EditorGUILayout.IntField("Allocated Actor Capacity", admission.AllocatedActorCapacity);
                EditorGUILayout.LongField("Rejected Actor Admissions", admission.RejectedAdmissionCount);
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
            ActorRegistrationPage page = ActorRegistrationPage.Create(
                world.ActorCount,
                actorPageIndex,
                ActorPageSize);
            actorPageIndex = page.PageIndex;

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(page.PageIndex == 0))
            {
                if (GUILayout.Button("Previous", GUILayout.Width(80f)))
                {
                    actorPageIndex--;
                }
            }

            EditorGUILayout.LabelField(
                $"Page {page.PageIndex + 1} of {page.PageCount} | indices {page.StartIndex}..{Math.Max(page.StartIndex, page.EndIndexExclusive - 1)}",
                EditorStyles.centeredGreyMiniLabel);

            using (new EditorGUI.DisabledScope(page.PageIndex >= page.PageCount - 1))
            {
                if (GUILayout.Button("Next", GUILayout.Width(80f)))
                {
                    actorPageIndex++;
                }
            }
            EditorGUILayout.EndHorizontal();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            int visibleCount = 0;
            for (int i = page.StartIndex; i < page.EndIndexExclusive; i++)
            {
                if (!world.TryGetActorRegistration(i, out WorldActorRegistration registration))
                {
                    continue;
                }

                Actor actor = registration.Actor;
                if (actor == null)
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
                EditorGUILayout.HelpBox("This page contains no live actor registrations.", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private void OnHierarchyChanged()
        {
            if (autoBind && host == null)
            {
                TryBindHost();
            }

            Repaint();
        }

        private void TryBindHost()
        {
            GameplayWorldHost nextHost = UnityEngine.Object.FindFirstObjectByType<GameplayWorldHost>(
                FindObjectsInactive.Include);
            if (host != nextHost)
            {
                host = nextHost;
                actorPageIndex = 0;
            }
        }
    }
}
