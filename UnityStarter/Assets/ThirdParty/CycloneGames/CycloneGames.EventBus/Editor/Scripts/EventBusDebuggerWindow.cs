using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CycloneGames.EventBus.Core;
using CycloneGames.EventBus.Runtime;

namespace CycloneGames.EventBus.Editor
{
    /// <summary>
    /// Read-only diagnostics window for a context registered through
    /// <see cref="EventBusEditorDiagnostics"/>. It uses a retained model: the bus list and counts are
    /// rebuilt only on an explicit refresh (or when the registered context changes), never per
    /// OnGUI repaint. It is read-only by design — triggering a generic test publish would require
    /// constructing an arbitrary <typeparamref name="T"/> by reflection, which this package bans for
    /// IL2CPP/AOT safety.
    /// </summary>
    public sealed class EventBusDebuggerWindow : EditorWindow
    {
        private EventBusContext _lastContext;
        private readonly List<Entry> _entries = new List<Entry>();
        private EventBusDiagnosticsSnapshot _snapshot;
        private bool _needsRebuild = true;

        // Formatted during Rebuild for the same reason: OnGUI is a repaint path, not a rebuild path.
        private string _activeBusLabel;
        private string _scopeLabel;
        private string _subscriptionLabel;
        private string _tombstoneLabel;
        private string _publishLabel;
        private string _droppedLabel;
        private string _errorLabel;
        private string _peakLabel;

        private struct Entry
        {
            public string TypeName;
            public EventBusSnapshot Snapshot;

            // Formatted once during Rebuild. OnGUI runs on every repaint (mouse move, scroll,
            // window interaction), and interpolating here would allocate a string per bus per
            // repaint for data that has not changed since the last refresh.
            public string Detail;
        }

        [MenuItem("Tools/CycloneGames/EventBus/Debugger")]
        private static void Open()
        {
            GetWindow<EventBusDebuggerWindow>("CycloneGames EventBus");
        }

        private void OnGUI()
        {
            EventBusContext context = EventBusEditorDiagnostics.Current;

            if (context == null)
            {
                _lastContext = null;
                _entries.Clear();
                EditorGUILayout.HelpBox(
                    "No context registered. Call EventBusEditorDiagnostics.Register(context) from "
                    + "editor-only code to observe a context.",
                    MessageType.Info);
                return;
            }

            if (context.IsDisposed)
            {
                // Drop the borrowed reference instead of rendering an all-zero world: a disposed
                // context has an empty bus map, and a static that keeps pointing at it would pin the
                // whole context (and its buses) for the lifetime of the Editor process.
                EventBusEditorDiagnostics.Clear();
                _lastContext = null;
                _entries.Clear();
                _needsRebuild = true;
                EditorGUILayout.HelpBox(
                    "The observed EventBusContext was disposed. The reference has been released; "
                    + "register a new context to observe it.",
                    MessageType.Warning);
                return;
            }

            if (context != _lastContext)
            {
                _lastContext = context;
                _needsRebuild = true;
            }

            if (_needsRebuild)
            {
                Rebuild(context);
                _needsRebuild = false;
            }

            EditorGUILayout.LabelField("Active buses", _activeBusLabel);
            EditorGUILayout.LabelField("Scopes", _scopeLabel);
            EditorGUILayout.LabelField("Subscriptions", _subscriptionLabel);
            EditorGUILayout.LabelField("Tombstones", _tombstoneLabel);
            EditorGUILayout.LabelField("Publish count", _publishLabel);
            EditorGUILayout.LabelField("Dropped (re-entrant)", _droppedLabel);
            EditorGUILayout.LabelField("Subscriber errors", _errorLabel);
            EditorGUILayout.LabelField("Peak subscriptions", _peakLabel);

            if (_snapshot.TombstoneCount > 0)
            {
                EditorGUILayout.HelpBox(
                    "Tombstones are dead slots left by unsubscribe. Compaction is automatic, so a "
                    + "small non-zero value is normal. A value that keeps climbing means subscribers "
                    + "are being added and removed faster than the compaction threshold triggers — "
                    + "gate the subscription with an activity flag instead of churning it.",
                    MessageType.Info);
            }

            if (_snapshot.DroppedReentrantCount > 0)
            {
                EditorGUILayout.HelpBox(
                    "Publishes were dropped because the re-entrancy depth ceiling was reached. That "
                    + "is a recursive publish chain, not a capacity problem: find the handler that "
                    + "publishes its own event type, directly or through a cycle.",
                    MessageType.Warning);
            }

            if (_snapshot.SubscriberErrorCount > 0)
            {
                EditorGUILayout.HelpBox(
                    "At least one subscriber threw. Every fault is counted and logged through the "
                    + "configured sink; check the console for the original throw sites.",
                    MessageType.Warning);
            }

            if (GUILayout.Button("Refresh"))
            {
                _needsRebuild = true;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Buses", EditorStyles.boldLabel);
            for (int index = 0; index < _entries.Count; index++)
            {
                Entry entry = _entries[index];
                EditorGUILayout.LabelField(entry.TypeName, entry.Detail);
            }
        }

        private void Rebuild(EventBusContext context)
        {
            _snapshot = context.GetDiagnosticsSnapshot();

            _activeBusLabel = _snapshot.ActiveBusCount.ToString();
            _scopeLabel = _snapshot.ScopeCount.ToString();
            _subscriptionLabel = _snapshot.SubscriptionCount.ToString();
            _tombstoneLabel = _snapshot.TombstoneCount.ToString();
            _publishLabel = _snapshot.PublishCount.ToString();
            _droppedLabel = _snapshot.DroppedReentrantCount.ToString();
            _errorLabel = _snapshot.SubscriberErrorCount.ToString();
            _peakLabel = _snapshot.PeakSubscriptionCount.ToString();

            _entries.Clear();
            IReadOnlyList<IEventBusDiagnostics> buses = context.GetRegisteredBuses();
            for (int index = 0; index < buses.Count; index++)
            {
                IEventBusDiagnostics bus = buses[index];
                EventBusSnapshot busSnapshot = bus.GetSnapshot();
                _entries.Add(new Entry
                {
                    TypeName = bus.EventTypeName,
                    Snapshot = busSnapshot,
                    Detail = FormatBusDetail(busSnapshot),
                });
            }
        }

        private static string FormatBusDetail(EventBusSnapshot snapshot)
        {
            return "subs=" + snapshot.SubscriptionCount.ToString()
                + ", tombstones=" + snapshot.TombstoneCount.ToString()
                + ", publishes=" + snapshot.PublishCount.ToString()
                + ", errors=" + snapshot.SubscriberErrorCount.ToString()
                + ", peak=" + snapshot.PeakSubscriptionCount.ToString()
                + ", cap=" + snapshot.Capacity.ToString()
                + ", depth=" + snapshot.DispatchDepth.ToString();
        }
    }

}
