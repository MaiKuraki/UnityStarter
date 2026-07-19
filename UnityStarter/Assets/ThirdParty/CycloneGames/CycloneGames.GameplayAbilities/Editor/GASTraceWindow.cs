using System.Text;
using CycloneGames.GameplayAbilities.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Editor
{
    public sealed class GASTraceWindow : EditorWindow
    {
        private const int MaxVisibleRows = 256;
        private const float ToolbarHeight = 34f;
        private const float StatusHeight = 28f;
        private const float DetailHeight = 152f;
        private const float RowHeight = 22f;
        private const float HeaderHeight = 24f;
        private const float LeftPadding = 10f;

        private static readonly GUIContent EnableContent = new GUIContent("Capture");
        private static readonly GUIContent PauseContent = new GUIContent("Pause");
        private static readonly GUIContent ClearContent = new GUIContent("Clear");
        private static readonly GUIContent SelectionContent = new GUIContent("Selection Only");
        private static readonly GUIContent ErrorsContent = new GUIContent("Problems Only");
        private static readonly GUIContent CapacityContent = new GUIContent("Capacity");
        private static readonly GUIContent EmptyContent = new GUIContent("No GAS trace events captured.");

        private static readonly string[] EventTypeNames =
        {
            "Ability Attempt",
            "Ability Blocked",
            "Ability Activated",
            "Ability Commit",
            "Ability Ended",
            "Ability Cancel",
            "Effect Attempt",
            "Effect Blocked",
            "Effect Applied",
            "Effect Executed",
            "Effect Removed",
            "Prediction Open",
            "Prediction OK",
            "Prediction Rollback",
            "Prediction Timeout",
            "Target OK",
            "Target Reject"
        };

        private static readonly string[] DecisionNames =
        {
            "",
            "Success",
            "Failed",
            "Blocked",
            "Rejected",
            "TimedOut",
            "RolledBack"
        };

        private static readonly string[] ReasonNames =
        {
            "",
            "Missing spec",
            "Already active",
            "Missing ability",
            "CanActivate failed",
            "Ability is ending",
            "Activation blocked tags",
            "Missing activation tags",
            "Missing source tags",
            "Source blocked tags",
            "Blocked by active ability",
            "Cooldown",
            "Cost",
            "Missing target tags",
            "Target blocked tags",
            "Immunity asset tags",
            "Immunity dynamic asset tags",
            "Immunity dynamic granted tags",
            "Missing application tags",
            "Application blocked tags",
            "Custom application requirement",
            "Stacking overflow",
            "Server rejected",
            "Prediction timeout",
            "Target data validation"
        };

        private static GUIStyle headerStyle;
        private static GUIStyle rowStyle;
        private static GUIStyle mutedStyle;
        private static GUIStyle badgeStyle;
        private static GUIStyle detailStyle;
        private static bool stylesInitialized;

        private readonly RowCache[] rowCaches = new RowCache[MaxVisibleRows];
        private readonly StringBuilder builder = new StringBuilder(256);
        private Vector2 scroll;
        private ulong selectedSequence;
        private GASTraceEvent selectedTraceEvent;
        private RowCache selectedRowCache;
        private bool hasSelectedTraceEvent;
        private bool paused;
        private bool selectionOnly;
        private bool problemsOnly;
        private int requestedCapacity = GASTrace.DefaultCapacity;
        private ulong lastObservedSequence;
        private double lastRepaintTime;
        private int cachedStatusCount = -1;
        private int cachedStatusCapacity = -1;
        private string cachedStatusText = string.Empty;

        private struct RowCache
        {
            public bool Initialized;
            public ulong Sequence;
            public string Frame;
            public string Time;
            public string Type;
            public string Target;
            public string Subject;
            public string Decision;
            public string Reason;
            public string Prediction;
            public string Spec;
            public string Level;
            public string ReconciliationId;
        }

        [MenuItem(GameplayAbilitiesEditorMenuPaths.Trace)]
        public static void ShowWindow()
        {
            var window = GetWindow<GASTraceWindow>("GAS Trace");
            window.minSize = new Vector2(900f, 600f);
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            requestedCapacity = GASTrace.Capacity;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            ClearSelection();
        }

        private void OnEditorUpdate()
        {
            if (paused)
            {
                return;
            }

            ulong latest = GASTrace.LatestSequence;
            if (latest == lastObservedSequence)
            {
                return;
            }

            lastObservedSequence = latest;
            double now = EditorApplication.timeSinceStartup;
            if (now - lastRepaintTime > 0.033)
            {
                lastRepaintTime = now;
                Repaint();
            }
        }

        private void OnGUI()
        {
            EnsureStyles();

            Rect rect = position;
            rect.x = 0f;
            rect.y = 0f;

            DrawToolbar(new Rect(0f, 0f, rect.width, ToolbarHeight));
            DrawStatus(new Rect(0f, ToolbarHeight, rect.width, StatusHeight));

            float detailY = rect.height - DetailHeight;
            DrawEventList(new Rect(0f, ToolbarHeight + StatusHeight, rect.width, detailY - ToolbarHeight - StatusHeight));
            DrawDetails(new Rect(0f, detailY, rect.width, DetailHeight));
        }

        private void DrawToolbar(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.14f, 1f));

            Rect item = new Rect(LeftPadding, rect.y + 6f, 92f, 22f);
            bool enabled = GUI.Toggle(item, GASTrace.Enabled, EnableContent, EditorStyles.toolbarButton);
            if (enabled != GASTrace.Enabled)
            {
                GASTrace.Enabled = enabled;
            }

            item.x += 100f;
            paused = GUI.Toggle(item, paused, PauseContent, EditorStyles.toolbarButton);

            item.x += 100f;
            if (GUI.Button(item, ClearContent, EditorStyles.toolbarButton))
            {
                GASTrace.Clear();
                ClearSelection();
                lastObservedSequence = 0;
                ClearRowCache();
            }

            item.x += 112f;
            item.width = 124f;
            selectionOnly = GUI.Toggle(item, selectionOnly, SelectionContent, EditorStyles.toolbarButton);

            item.x += 132f;
            item.width = 116f;
            problemsOnly = GUI.Toggle(item, problemsOnly, ErrorsContent, EditorStyles.toolbarButton);

            item.x = rect.width - 206f;
            item.width = 62f;
            GUI.Label(item, CapacityContent, mutedStyle);

            item.x += 66f;
            item.width = 72f;
            int nextCapacity = EditorGUI.IntField(item, requestedCapacity);
            requestedCapacity = Mathf.Clamp(nextCapacity, 256, GASTrace.MaxCapacity);

            item.x += 78f;
            item.width = 54f;
            if (GUI.Button(item, "Apply", EditorStyles.toolbarButton))
            {
                GASTrace.SetCapacity(requestedCapacity);
                ClearSelection();
                ClearRowCache();
            }
        }

        private void DrawStatus(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.19f, 1f));

            float availableWidth = Mathf.Max(0f, rect.width - LeftPadding * 2f);
            float leftWidth = Mathf.Min(330f, availableWidth * 0.42f);
            float middleWidth = Mathf.Min(220f, availableWidth * 0.28f);
            float rightWidth = Mathf.Max(110f, availableWidth - leftWidth - middleWidth - 24f);

            Rect item = new Rect(LeftPadding, rect.y + 5f, leftWidth, 18f);
            GUI.Label(item, GASTrace.Enabled ? "Capturing GAS trace events." : "Trace recorder is disabled.", GASTrace.Enabled ? rowStyle : mutedStyle);

            item.x += leftWidth + 12f;
            item.width = middleWidth;
            int count = GASTrace.Count;
            int capacity = GASTrace.Capacity;
            if (count != cachedStatusCount || capacity != cachedStatusCapacity)
            {
                cachedStatusCount = count;
                cachedStatusCapacity = capacity;
                builder.Length = 0;
                builder.Append("Events: ").Append(count).Append(" / ").Append(capacity);
                cachedStatusText = builder.ToString();
            }
            GUI.Label(item, cachedStatusText, mutedStyle);

            item.x += middleWidth + 12f;
            item.width = rightWidth;
            GUI.Label(item, "Default off; enable while diagnosing.", mutedStyle);
        }

        private void DrawEventList(Rect rect)
        {
            Rect header = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            EditorGUI.DrawRect(header, new Color(0.11f, 0.11f, 0.12f, 1f));
            DrawColumns(header, true, default);

            Rect view = new Rect(rect.x, rect.y + HeaderHeight, rect.width, rect.height - HeaderHeight);
            int sourceCount = GASTrace.Count;
            float contentHeight = Mathf.Max(view.height, sourceCount * RowHeight);
            Rect content = new Rect(0f, 0f, view.width - 16f, contentHeight);
            scroll = GUI.BeginScrollView(view, scroll, content);

            int drawn = 0;
            int row = 0;
            GameObject selectedObject = Selection.activeGameObject;
            for (int recentIndex = 0; recentIndex < sourceCount && drawn < MaxVisibleRows; recentIndex++)
            {
                if (!GASTrace.TryGetRecent(recentIndex, out var traceEvent))
                {
                    continue;
                }

                if (!PassesFilters(in traceEvent, selectedObject))
                {
                    continue;
                }

                float y = row * RowHeight;
                if (y + RowHeight < scroll.y)
                {
                    row++;
                    continue;
                }

                if (y > scroll.y + view.height)
                {
                    break;
                }

                Rect rowRect = new Rect(0f, y, content.width, RowHeight);
                DrawColumns(rowRect, false, traceEvent);
                drawn++;
                row++;
            }

            if (sourceCount == 0)
            {
                GUI.Label(new Rect(LeftPadding, 18f, 300f, 20f), EmptyContent, mutedStyle);
            }

            GUI.EndScrollView();
        }

        private void DrawColumns(Rect rect, bool isHeader, GASTraceEvent traceEvent)
        {
            if (!isHeader)
            {
                bool selected = hasSelectedTraceEvent && traceEvent.Sequence == selectedSequence;
                EditorGUI.DrawRect(rect, selected ? new Color(0.22f, 0.34f, 0.52f, 1f) : RowColor(traceEvent));
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    selectedSequence = traceEvent.Sequence;
                    selectedTraceEvent = traceEvent;
                    selectedRowCache = GetRowCache(in traceEvent);
                    hasSelectedTraceEvent = true;
                }
            }

            RowCache cache = default;
            if (!isHeader)
            {
                cache = GetRowCache(in traceEvent);
            }

            float x = LeftPadding;
            DrawCell(new Rect(x, rect.y + 3f, 64f, 18f), isHeader ? "Frame" : cache.Frame, isHeader);
            x += 70f;
            DrawCell(new Rect(x, rect.y + 3f, 62f, 18f), isHeader ? "Time" : cache.Time, isHeader);
            x += 70f;
            DrawCell(new Rect(x, rect.y + 3f, 138f, 18f), isHeader ? "Event" : cache.Type, isHeader);
            x += 146f;
            DrawCell(new Rect(x, rect.y + 3f, 150f, 18f), isHeader ? "Target" : cache.Target, isHeader);
            x += 158f;
            DrawCell(new Rect(x, rect.y + 3f, 172f, 18f), isHeader ? "Subject" : cache.Subject, isHeader);
            x += 180f;
            DrawCell(new Rect(x, rect.y + 3f, 88f, 18f), isHeader ? "Decision" : cache.Decision, isHeader);
            x += 96f;
            DrawCell(new Rect(x, rect.y + 3f, 172f, 18f), isHeader ? "Reason" : cache.Reason, isHeader);
            x += 180f;
            DrawCell(new Rect(x, rect.y + 3f, 116f, 18f), isHeader ? "Prediction" : cache.Prediction, isHeader);
        }

        private void DrawDetails(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.13f, 1f));
            Rect title = new Rect(LeftPadding, rect.y + 8f, rect.width - 20f, 20f);
            GUI.Label(title, "Event Details", headerStyle);

            if (!hasSelectedTraceEvent)
            {
                GUI.Label(new Rect(LeftPadding, rect.y + 36f, rect.width - 20f, 20f), "Select an event row to inspect the decision context.", mutedStyle);
                return;
            }

            GASTraceEvent traceEvent = selectedTraceEvent;
            RowCache cache = selectedRowCache;
            float y = rect.y + 38f;
            DrawDetailLine(rect.x + LeftPadding, y, "Event", cache.Type);
            DrawDetailLine(rect.x + LeftPadding + 260f, y, "Decision", cache.Decision);
            DrawDetailLine(rect.x + LeftPadding + 520f, y, "Reason", cache.Reason);

            y += 26f;
            DrawDetailLine(rect.x + LeftPadding, y, "Target", cache.Target);
            DrawDetailLine(rect.x + LeftPadding + 260f, y, "Source", GetASCName(traceEvent.Source));
            DrawDetailLine(rect.x + LeftPadding + 520f, y, "Subject", cache.Subject);

            y += 26f;
            DrawDetailLine(rect.x + LeftPadding, y, "Spec", cache.Spec);
            DrawDetailLine(rect.x + LeftPadding + 260f, y, "Level", cache.Level);
            DrawDetailLine(rect.x + LeftPadding + 520f, y, "Reconciliation ID", cache.ReconciliationId);

            y += 26f;
            DrawDetailLine(rect.x + LeftPadding, y, "Prediction", cache.Prediction);
        }

        private static void DrawDetailLine(float x, float y, string label, string value)
        {
            GUI.Label(new Rect(x, y, 88f, 18f), label, mutedStyle);
            GUI.Label(new Rect(x + 90f, y, 160f, 18f), value, detailStyle);
        }

        private static void DrawCell(Rect rect, string text, bool isHeader)
        {
            GUI.Label(rect, text, isHeader ? headerStyle : rowStyle);
        }

        private bool PassesFilters(in GASTraceEvent traceEvent, GameObject selectedObject)
        {
            if (problemsOnly &&
                traceEvent.Decision != GASTraceDecision.Blocked &&
                traceEvent.Decision != GASTraceDecision.Failed &&
                traceEvent.Decision != GASTraceDecision.Rejected &&
                traceEvent.Decision != GASTraceDecision.TimedOut &&
                traceEvent.Decision != GASTraceDecision.RolledBack)
            {
                return false;
            }

            if (!selectionOnly || selectedObject == null)
            {
                return true;
            }

            if (traceEvent.Target == null)
            {
                return false;
            }

            if (traceEvent.Target.AvatarGameObject == selectedObject || traceEvent.Target.OwnerUnityObject == selectedObject)
            {
                return true;
            }

            return traceEvent.Target.OwnerUnityObject is Component ownerComponent &&
                ownerComponent.gameObject == selectedObject;
        }

        private RowCache GetRowCache(in GASTraceEvent traceEvent)
        {
            int cacheIndex = (int)(traceEvent.Sequence % (ulong)rowCaches.Length);
            ref RowCache cache = ref rowCaches[cacheIndex];
            if (cache.Initialized && cache.Sequence == traceEvent.Sequence)
            {
                return cache;
            }

            cache.Initialized = true;
            cache.Sequence = traceEvent.Sequence;
            cache.Frame = traceEvent.Frame.ToString();
            cache.Time = traceEvent.Time.ToString("0.00");
            cache.Type = GetEnumName(EventTypeNames, (int)traceEvent.Type);
            cache.Target = GetASCName(traceEvent.Target);
            cache.Subject = GetSubjectName(in traceEvent);
            cache.Decision = GetEnumName(DecisionNames, (int)traceEvent.Decision);
            cache.Reason = GetEnumName(ReasonNames, (int)traceEvent.Reason);
            cache.Prediction = BuildPredictionText(in traceEvent);
            cache.Spec = FormatOptionalInt(traceEvent.AbilitySpecHandle);
            cache.Level = FormatOptionalInt(traceEvent.Level);
            cache.ReconciliationId = FormatOptionalInt(traceEvent.ReconciliationId);
            return cache;
        }

        private static string GetEnumName(string[] names, int index)
        {
            return (uint)index < (uint)names.Length ? names[index] : "<Unknown>";
        }

        private static string GetASCName(AbilitySystemComponent asc)
        {
            if (asc == null)
            {
                return "";
            }

            if (asc.AvatarGameObject != null)
            {
                return asc.AvatarGameObject.name;
            }

            return asc.OwnerActor?.ToString() ?? "<ASC>";
        }

        private static string GetSubjectName(in GASTraceEvent traceEvent)
        {
            if (traceEvent.AbilityDefinition != null)
            {
                return traceEvent.AbilityDefinition.Name;
            }

            if (traceEvent.Effect != null)
            {
                return traceEvent.Effect.Name;
            }

            return "";
        }

        private string BuildPredictionText(in GASTraceEvent traceEvent)
        {
            if (traceEvent.PredictionKey == 0)
            {
                return "";
            }

            builder.Length = 0;
            builder.Append(traceEvent.PredictionKey);
            if (traceEvent.PredictionInputSequence != 0)
            {
                builder.Append(" / ").Append(traceEvent.PredictionInputSequence);
            }
            return builder.ToString();
        }

        private static string FormatOptionalInt(int value)
        {
            return value == 0 ? "" : value.ToString();
        }

        private static Color RowColor(GASTraceEvent traceEvent)
        {
            switch (traceEvent.Decision)
            {
                case GASTraceDecision.Blocked:
                case GASTraceDecision.Rejected:
                case GASTraceDecision.TimedOut:
                case GASTraceDecision.RolledBack:
                    return new Color(0.24f, 0.12f, 0.12f, 1f);
                case GASTraceDecision.Success:
                    return new Color(0.12f, 0.18f, 0.14f, 1f);
                default:
                    return new Color(0.16f, 0.16f, 0.17f, 1f);
            }
        }

        private void ClearRowCache()
        {
            for (int i = 0; i < rowCaches.Length; i++)
            {
                rowCaches[i] = default;
            }
        }

        private void ClearSelection()
        {
            selectedSequence = 0;
            selectedTraceEvent = default;
            selectedRowCache = default;
            hasSelectedTraceEvent = false;
        }

        private static void EnsureStyles()
        {
            if (stylesInitialized)
            {
                return;
            }

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.86f, 0.88f, 0.92f, 1f) },
                clipping = TextClipping.Clip
            };
            rowStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.82f, 0.84f, 0.88f, 1f) },
                clipping = TextClipping.Clip
            };
            mutedStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.55f, 0.58f, 0.62f, 1f) },
                clipping = TextClipping.Clip
            };
            badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip
            };
            detailStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.92f, 0.93f, 0.95f, 1f) },
                clipping = TextClipping.Clip
            };
            stylesInitialized = true;
        }
    }
}
